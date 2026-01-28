using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace cadstockv2
{
    internal sealed class StockQuoteService
    {
        public static StockQuoteService Instance { get; } = new StockQuoteService();

        private readonly object _lock = new object();

        // ✅ 内部统一保存为 Sina 代码：sh600000 / sz000001 / sh000001(上证指数) ...
        private readonly List<string> _symbols = new List<string>();
        private readonly Dictionary<string, StockQuote> _latest =
            new Dictionary<string, StockQuote>(StringComparer.OrdinalIgnoreCase);

        private Timer _timer;
        private HttpClient _http;

        private int _tickRunning = 0;

        public DateTime? LastUpdate { get; private set; }
        public string LastError { get; private set; }

        public event Action DataUpdated;

        // 刷新周期（毫秒）——你要改刷新频率就改这里，例如 5000 = 5 秒
        private const int RefreshIntervalMs = 10_000;

        private StockQuoteService()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public void Start()
        {
            lock (_lock)
            {
                EnsureHttp_NoLock();

                if (_timer == null)
                {
                    // 立即跑一次，然后每 RefreshIntervalMs 刷新
                    _timer = new Timer(async _ => await TickSafeAsync().ConfigureAwait(false), null, 0, RefreshIntervalMs);
                }

                // 第一次启动加载 symbols
                if (_symbols.Count == 0)
                {
                    LoadSymbolsFromDisk_NoLock(useDefaultsIfEmpty: true, saveIfDefaultInserted: true);
                    CleanupLatest_NoLock();
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                _http?.Dispose();
                _http = null;
            }
        }

        /// <summary>
        /// ✅ 重新从 txt 加载股票列表（用于：你手工编辑 txt 后，执行 CADSTOCKV2REFRESH 立即生效）
        /// </summary>
        public void ReloadSymbolsFromDisk(bool forceRefresh = true)
        {
            lock (_lock)
            {
                LoadSymbolsFromDisk_NoLock(useDefaultsIfEmpty: true, saveIfDefaultInserted: false);
                CleanupLatest_NoLock();
            }

            // 先通知 UI：让移除的股票立刻消失（即便网络还没刷新回来）
            FireUpdated();

            if (forceRefresh) ForceRefresh();
        }

        /// <summary>强制立刻刷新一次（不阻塞 UI）</summary>
        public void ForceRefresh()
        {
            // 确保已初始化 HttpClient（否则只调用 REFRESH 时会 NRE）
            Start();
            Task.Run(async () => await TickSafeAsync().ConfigureAwait(false));
        }

        /// <summary>面板/下拉菜单用：按 symbols 顺序输出</summary>
        public List<StockQuote> GetSnapshot()
        {
            lock (_lock)
            {
                var result = new List<StockQuote>();

                for (int i = 0; i < _symbols.Count; i++)
                {
                    var s = _symbols[i];
                    if (_latest.TryGetValue(s, out var q) && q != null)
                        result.Add(q);
                }

                return result;
            }
        }

        public StockQuote GetSnapshot(string symbol)
        {
            symbol = NormalizeSymbol(symbol);
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            lock (_lock)
            {
                return _latest.TryGetValue(symbol, out var q) ? q : null;
            }
        }

        public IReadOnlyList<string> GetSymbols()
        {
            lock (_lock) return _symbols.ToList();
        }

        public void SetSymbols(IEnumerable<string> symbols)
        {
            lock (_lock)
            {
                _symbols.Clear();

                // ✅ 支持：sh600000 / sz000001 / 600000 / 0.000001 / 1.600000 / 600000.SH 等
                var list = (symbols ?? Array.Empty<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(NormalizeSymbol)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                // 去重但保序
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in list)
                {
                    if (seen.Add(s))
                        _symbols.Add(s);
                }

                CleanupLatest_NoLock();
                SaveSymbols_NoLock();
            }

            FireUpdated();
            ForceRefresh();
        }

        private void EnsureHttp_NoLock()
        {
            if (_http != null) return;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) cadstockv2");
            try { _http.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn/"); } catch { }
        }

        private async Task TickSafeAsync()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1) return;

            try
            {
                await TickAsync().ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                lock (_lock)
                {
                    LastError = ex.GetType().Name + ": " + ex.Message;
                    LastUpdate = null;
                }
                FireUpdated();
            }
            finally
            {
                Interlocked.Exchange(ref _tickRunning, 0);
            }
        }

        private async Task TickAsync()
        {
            List<string> list;
            lock (_lock)
            {
                list = _symbols.ToList();
            }

            if (list.Count == 0)
            {
                lock (_lock)
                {
                    LastError = "股票列表为空（symbols=0）。请先设置股票代码。";
                    LastUpdate = null;
                }
                FireUpdated();
                return;
            }

            // ✅ 一次最多 50 个，避免限流
            var batch = list.Take(50).Select(ToSinaCode).ToList();
            var url = "https://hq.sinajs.cn/list=" + string.Join(",", batch);

            string text;
            try
            {
                using (var resp = await _http.GetAsync(url).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        lock (_lock)
                        {
                            LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                            LastUpdate = null;
                        }
                        FireUpdated();
                        return;
                    }

                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    text = DecodeSina(bytes);
                }
            }
            catch (System.Exception ex)
            {
                lock (_lock)
                {
                    LastError = ex.GetType().Name + ": " + ex.Message;
                    LastUpdate = null;
                }
                FireUpdated();
                return;
            }

            var parsed = ParseSinaResponse(text);

            int ok = 0;
            lock (_lock)
            {
                foreach (var q in parsed)
                {
                    if (q == null) continue;
                    _latest[q.Symbol] = q;
                    ok++;
                }

                if (ok > 0)
                {
                    LastUpdate = DateTime.Now;
                    LastError = null;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(LastError))
                        LastError = "Sina 返回为空/解析失败（ok=0）";
                    LastUpdate = null;
                }
            }

            FireUpdated();
        }

        private static string DecodeSina(byte[] bytes)
        {
            try { return Encoding.GetEncoding(936).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }

        private List<StockQuote> ParseSinaResponse(string text)
        {
            var list = new List<StockQuote>();
            if (string.IsNullOrWhiteSpace(text))
            {
                lock (_lock) LastError = "Sina 返回空内容（body 为空）";
                return list;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (!line.StartsWith("var hq_str_", StringComparison.OrdinalIgnoreCase)) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring("var ".Length, eq - "var ".Length).Trim(); // hq_str_sh600000
                var code = key.Replace("hq_str_", "").Trim(); // sh600000

                int q1 = line.IndexOf('"', eq);
                if (q1 < 0) continue;
                int q2 = line.LastIndexOf('"');
                if (q2 <= q1) continue;

                var payload = line.Substring(q1 + 1, q2 - q1 - 1);
                if (string.IsNullOrWhiteSpace(payload)) continue;

                var fields = payload.Split(',');
                if (fields.Length < 4) continue;

                var name = fields[0];
                decimal prev = ParseDecimal(fields, 2);
                decimal price = ParseDecimal(fields, 3);

                // ✅ 这里 Symbol 也用带前缀的 canonical
                var symbol = NormalizeSymbol(code);
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                list.Add(new StockQuote
                {
                    Symbol = symbol,
                    Name = string.IsNullOrWhiteSpace(name) ? symbol : name,
                    Price = price,
                    PrevClose = prev
                });
            }

            if (list.Count == 0)
            {
                lock (_lock)
                {
                    LastError = "Sina 解析为 0 条。响应前 120 字："
                                + (text.Length > 120 ? text.Substring(0, 120) : text);
                }
            }

            return list;
        }

        private static decimal ParseDecimal(string[] fields, int idx)
        {
            try
            {
                if (idx < 0 || idx >= fields.Length) return 0m;
                return decimal.TryParse(fields[idx], out var v) ? v : 0m;
            }
            catch { return 0m; }
        }

        /// <summary>
        /// ✅ 规范化为 Sina 代码（shxxxxxx / szxxxxxx）
        /// - 支持：sh600000 / sz000001
        /// - 支持：600000（自动推断；指数请显式写 sh000001）
        /// - 支持：1.600000 / 0.000001（东财 secid）
        /// - 支持：600000.SH / 000001.SZ
        /// </summary>
        private static string NormalizeSymbol(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;

            // 后缀形式：600000.SH / 000001.SZ
            if (s.EndsWith(".SH", StringComparison.OrdinalIgnoreCase))
                s = "sh" + s.Substring(0, s.Length - 3);
            else if (s.EndsWith(".SZ", StringComparison.OrdinalIgnoreCase))
                s = "sz" + s.Substring(0, s.Length - 3);

            // 东财 secid：1.600000 / 0.000001
            if (s.Length > 2 && s[1] == '.' && (s[0] == '0' || s[0] == '1'))
            {
                var p = s[0] == '1' ? "sh" : "sz";
                s = p + s.Substring(2);
            }

            // 已带前缀
            if (s.Length >= 2)
            {
                var p2 = s.Substring(0, 2);
                if (string.Equals(p2, "sh", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p2, "sz", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = s.Substring(2).Trim();
                    if (rest.All(char.IsDigit))
                        return (p2.ToLowerInvariant() + rest);
                    // 如果 rest 不是纯数字，放弃
                    return null;
                }
            }

            // 纯数字：自动推断（指数请显式写 sh000001）
            if (s.All(char.IsDigit))
            {
                if (s.StartsWith("6") || s.StartsWith("5") || s.StartsWith("9"))
                    return "sh" + s;
                return "sz" + s;
            }

            return null;
        }

        private static string ToSinaCode(string symbol)
        {
            // _symbols 内已经是 canonical 了，这里再保险一下
            var s = NormalizeSymbol(symbol);
            return s ?? "";
        }

        private void FireUpdated()
        {
            try { DataUpdated?.Invoke(); } catch { }
        }

        // ----------------- symbols 持久化（每行一个，支持 sh/sz 前缀） -----------------
        private static string SymbolsFile =>
            Path.Combine(Path.GetTempPath(), "cadstockv2_symbols.txt");

        private void LoadSymbolsFromDisk_NoLock(bool useDefaultsIfEmpty, bool saveIfDefaultInserted)
        {
            try
            {
                var tmp = new List<string>();

                if (File.Exists(SymbolsFile))
                {
                    var lines = File.ReadAllLines(SymbolsFile, Encoding.UTF8);

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in lines)
                    {
                        var sym = NormalizeSymbol(line);
                        if (string.IsNullOrWhiteSpace(sym)) continue;
                        if (seen.Add(sym))
                            tmp.Add(sym);
                    }
                }

                if (tmp.Count == 0 && useDefaultsIfEmpty)
                {
                    // 默认给几个
                    tmp.AddRange(new[] { "sh600000", "sz000001", "sh600519" });

                    if (saveIfDefaultInserted)
                    {
                        _symbols.Clear();
                        _symbols.AddRange(tmp);
                        SaveSymbols_NoLock();
                        return;
                    }
                }

                _symbols.Clear();
                _symbols.AddRange(tmp);
            }
            catch
            {
                // ignore
            }
        }

        private void SaveSymbols_NoLock()
        {
            try
            {
                File.WriteAllLines(SymbolsFile, _symbols, Encoding.UTF8);
            }
            catch { }
        }

        private void CleanupLatest_NoLock()
        {
            var keep = new HashSet<string>(_symbols, StringComparer.OrdinalIgnoreCase);
            var keys = _latest.Keys.ToList();
            foreach (var k in keys)
            {
                if (!keep.Contains(k))
                    _latest.Remove(k);
            }
        }
    }

    internal sealed class StockQuote
    {
        public string Symbol { get; set; }   // canonical: shxxxxxx / szxxxxxx
        public string Name { get; set; }
        public decimal Price { get; set; }
        public decimal PrevClose { get; set; }

        // 显示用：去掉 sh/sz
        public string SymbolShort
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Symbol)) return "";
                if (Symbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                    Symbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
                {
                    return Symbol.Length > 2 ? Symbol.Substring(2) : Symbol;
                }
                return Symbol;
            }
        }

        public decimal ChangePercent
        {
            get
            {
                if (PrevClose == 0m) return 0m;
                return (Price - PrevClose) / PrevClose * 100m;
            }
        }

        public override string ToString()
            => $"{Symbol} {Name} {Price}";
    }
}
