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
        private readonly List<string> _symbols = new List<string>(); // "600000" / "000001" / etc
        private readonly Dictionary<string, StockQuote> _latest = new Dictionary<string, StockQuote>(StringComparer.OrdinalIgnoreCase);

        private Timer _timer;
        private HttpClient _http;

        private int _tickRunning = 0;

        public DateTime? LastUpdate { get; private set; }
        public string LastError { get; private set; }

        public event Action DataUpdated;

        // ✅ 刷新周期（毫秒）。10_000 = 10秒；要改刷新频率，改这里即可
        private const int TimerPeriodMs = 10_000;

        private StockQuoteService()
        {
            // ✅ 强制开启 TLS1.2（Sina 也走 HTTPS，保险）
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_http == null)
                {
                    var handler = new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                    };

                    _http = new HttpClient(handler)
                    {
                        Timeout = TimeSpan.FromSeconds(6)
                    };

                    // Sina 常见：带 UA + Referer 更稳
                    _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) cadstockv2");
                    try { _http.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn/"); } catch { }
                }

                if (_timer == null)
                {
                    // 立即跑一次，然后每 TimerPeriodMs 刷新一次
                    _timer = new Timer(async _ => await TickSafeAsync().ConfigureAwait(false), null, 0, TimerPeriodMs);
                }

                if (_symbols.Count == 0)
                {
                    LoadSymbols();
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

        /// <summary>强制立刻刷新一次</summary>
        public void ForceRefresh()
        {
            Task.Run(async () => await TickSafeAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// ✅ 给 UI 用：按 _symbols 顺序输出（保证下拉菜单/面板顺序一致）
        /// </summary>
        public List<StockQuote> GetSnapshot()
        {
            lock (_lock)
            {
                var result = new List<StockQuote>();

                // 按 symbols 顺序输出
                for (int i = 0; i < _symbols.Count; i++)
                {
                    var s = _symbols[i];
                    StockQuote q;
                    if (_latest.TryGetValue(s, out q) && q != null)
                        result.Add(q);
                }

                return result;
            }
        }

        public StockQuote GetSnapshot(string symbol)
        {
            symbol = NormalizeSymbol(symbol);
            lock (_lock)
            {
                StockQuote q;
                if (_latest.TryGetValue(symbol, out q)) return q;
                return null;
            }
        }

        /// <summary>
        /// ✅ 设置股票列表：写入 _symbols，且清理 _latest 中已移除的股票（避免 UI 还显示旧的）
        /// </summary>
        public void SetSymbols(IEnumerable<string> symbols)
        {
            lock (_lock)
            {
                _symbols.Clear();
                _symbols.AddRange(symbols
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(NormalizeSymbol)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                // ✅ 清理 _latest 里已被移除的股票，否则 UI 还会显示旧的
                var keep = new HashSet<string>(_symbols, StringComparer.OrdinalIgnoreCase);
                var keys = _latest.Keys.ToList();
                foreach (var k in keys)
                {
                    if (!keep.Contains(k))
                        _latest.Remove(k);
                }

                SaveSymbols();
            }

            ForceRefresh();
        }

        private async Task TickSafeAsync()
        {
            // 防止 Timer 重入
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

            // ✅ Sina 支持一次请求多个：sh600000,sz000001...
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
            try
            {
                return Encoding.GetEncoding(936).GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
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
                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                var fields = payload.Split(',');
                if (fields.Length < 4) continue;

                var name = fields[0];
                decimal prev = ParseDecimal(fields, 2);
                decimal price = ParseDecimal(fields, 3);

                var symbol = NormalizeSymbol(code);

                var q = new StockQuote
                {
                    Symbol = symbol,
                    Name = string.IsNullOrWhiteSpace(name) ? symbol : name,
                    Price = price,
                    PrevClose = prev
                };

                list.Add(q);
            }

            if (list.Count == 0)
            {
                lock (_lock)
                {
                    LastError = "Sina 解析为 0 条。可能被拦/返回格式变化。响应前 120 字："
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
                decimal v;
                if (decimal.TryParse(fields[idx], out v)) return v;
                return 0m;
            }
            catch { return 0m; }
        }

        private static string NormalizeSymbol(string s)
        {
            s = (s ?? "").Trim();

            if (s.Length >= 2)
            {
                var p = s.Substring(0, 2);
                if (string.Equals(p, "sh", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "sz", StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(2);
                }
            }

            if (s.Length > 2 && s[1] == '.' && (s[0] == '0' || s[0] == '1'))
                s = s.Substring(2);

            return s;
        }

        private static string ToSinaCode(string symbol)
        {
            symbol = NormalizeSymbol(symbol);

            if (symbol.StartsWith("6") || symbol.StartsWith("5") || symbol.StartsWith("9"))
                return "sh" + symbol;

            return "sz" + symbol;
        }

        private void FireUpdated()
        {
            // 1) 通知外部（EntryPoint 订阅者）
            try { DataUpdated?.Invoke(); } catch { }

            // 2) ✅ 兜底：直接通知面板刷新（避免“下拉菜单更新了但面板不刷”的情况）
            try { PaletteHost.NotifyQuotesUpdated(); } catch { }
        }

        // ----------------- symbols 持久化（简单放到 %TEMP%） -----------------
        private static string SymbolsFile =>
            Path.Combine(Path.GetTempPath(), "cadstockv2_symbols.txt");

        private void LoadSymbols()
        {
            try
            {
                if (File.Exists(SymbolsFile))
                {
                    var lines = File.ReadAllLines(SymbolsFile, Encoding.UTF8)
                        .Select(NormalizeSymbol)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    _symbols.Clear();
                    _symbols.AddRange(lines);
                }

                if (_symbols.Count == 0)
                {
                    _symbols.AddRange(new[] { "600000", "000001", "600519" });
                    SaveSymbols();
                }
            }
            catch { }
        }

        private void SaveSymbols()
        {
            try
            {
                File.WriteAllLines(SymbolsFile, _symbols, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal sealed class StockQuote
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public decimal PrevClose { get; set; }

        public string SymbolShort => Symbol;

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
