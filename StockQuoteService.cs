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

        // ✅ 统一存“规范化后的代码”：sh600000 / sz000001 / sh000001(上证指数)
        private readonly List<string> _symbols = new List<string>();
        private readonly Dictionary<string, StockQuote> _latest =
            new Dictionary<string, StockQuote>(StringComparer.OrdinalIgnoreCase);

        private Timer _timer;
        private HttpClient _http;
        private int _tickRunning = 0;

        public DateTime? LastUpdate { get; private set; }
        public string LastError { get; private set; }

        public event Action DataUpdated;

        private StockQuoteService()
        {
            // ✅ 强制开启 TLS1.2
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
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
                    _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

                    _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) cadstockv2");
                    try { _http.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn/"); } catch { }
                }

                if (_timer == null)
                {
                    // ✅ 刷新周期就在这里：10_000 = 10秒（改成 5_000 就是 5秒）
                    _timer = new Timer(async _ => await TickSafeAsync().ConfigureAwait(false),
                        null, 0, 10_000);
                }

                if (_symbols.Count == 0)
                {
                    LoadSymbols(allowDefaultIfMissing: true);
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

        /// <summary>给 UI / 命令用：取当前 symbols（按顺序）</summary>
        public List<string> GetSymbols()
        {
            lock (_lock) return _symbols.ToList();
        }

        /// <summary>面板/下拉菜单用：按 symbols 顺序输出；缺数据也会给占位，保证“列表”立刻变化</summary>
        public List<StockQuote> GetSnapshot()
        {
            lock (_lock)
            {
                var result = new List<StockQuote>(_symbols.Count);

                for (int i = 0; i < _symbols.Count; i++)
                {
                    var s = _symbols[i];
                    StockQuote q;
                    if (_latest.TryGetValue(s, out q) && q != null)
                        result.Add(q);
                    else
                        result.Add(new StockQuote
                        {
                            Symbol = s,
                            Name = s,      // 没数据时先显示代码
                            Price = 0m,
                            PrevClose = 0m
                        });
                }

                return result;
            }
        }

        /// <summary>兼容你现有 DropdownCommands：顺带拿 LastUpdate</summary>
        public List<StockQuote> GetSnapshot(out DateTime last)
        {
            lock (_lock) last = LastUpdate ?? DateTime.MinValue;
            return GetSnapshot();
        }

        /// <summary>取单只</summary>
        public StockQuote GetSnapshot(string symbol)
        {
            symbol = NormalizeSymbol(symbol);
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            lock (_lock)
            {
                StockQuote q;
                if (_latest.TryGetValue(symbol, out q)) return q;
                return null;
            }
        }

        /// <summary>
        /// 设置股票列表（会保存到 txt），并立即触发 UI 刷新 + 拉取
        /// </summary>
        public void SetSymbols(IEnumerable<string> symbols)
        {
            lock (_lock)
            {
                _symbols.Clear();

                foreach (var s0 in (symbols ?? Array.Empty<string>()))
                {
                    var s = NormalizeSymbol(s0);
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (!_symbols.Contains(s, StringComparer.OrdinalIgnoreCase))
                        _symbols.Add(s);
                }

                CleanupLatest_NoLock();
                SaveSymbols();
            }

            FireUpdated();   // 先让 UI 立刻“列表变化”
            ForceRefresh();  // 再异步拉取行情
        }

        /// <summary>
        /// ✅ 手动编辑 txt 后调用它：重新读取 symbols，并可选择是否强制拉取
        /// </summary>
        public void ReloadSymbolsFromDisk(bool forceRefresh)
        {
            lock (_lock)
            {
                LoadSymbols(allowDefaultIfMissing: true);
                CleanupLatest_NoLock();
            }

            FireUpdated();          // 立刻让面板/下拉“列表”更新
            if (forceRefresh) ForceRefresh();
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
            lock (_lock) list = _symbols.ToList();

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

            // 一次最多 50 个，避免被限流
            var batch = list.Take(50).Select(ToSinaCode).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
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
            try { return Encoding.GetEncoding(936).GetString(bytes); }  // GBK
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
                var code = key.Replace("hq_str_", "").Trim();                         // sh600000

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
                decimal v;
                if (decimal.TryParse(fields[idx], out v)) return v;
                return 0m;
            }
            catch { return 0m; }
        }

        /// <summary>
        /// ✅ 关键：规范化为 shXXXXXX / szXXXXXX
        /// 支持输入：600000、sh600000、SZ000001、1.600000、0.000001
        /// 不带前缀时：6/5/9 开头=>sh，其它=>sz
        /// </summary>
        internal static string NormalizeSymbol(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "";

            string prefix = null;

            if (s.Length >= 2)
            {
                var p = s.Substring(0, 2);
                if (p.Equals("sh", StringComparison.OrdinalIgnoreCase)) { prefix = "sh"; s = s.Substring(2); }
                else if (p.Equals("sz", StringComparison.OrdinalIgnoreCase)) { prefix = "sz"; s = s.Substring(2); }
            }

            // 兼容 1.600000 / 0.000001（东财 secid）
            if (prefix == null && s.Length > 2 && s[1] == '.' && (s[0] == '0' || s[0] == '1'))
            {
                prefix = (s[0] == '1') ? "sh" : "sz";
                s = s.Substring(2);
            }

            s = s.Trim();
            if (s.Length == 0) return "";

            // 只取数字部分（防止用户输入带后缀）
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digits)) return "";

            if (prefix == null)
            {
                if (digits.StartsWith("6") || digits.StartsWith("5") || digits.StartsWith("9"))
                    prefix = "sh";
                else
                    prefix = "sz";
            }

            return prefix + digits;
        }

        private static string ToSinaCode(string symbol)
        {
            var s = NormalizeSymbol(symbol);
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.ToLowerInvariant(); // sinajs 用小写 sh/sz
        }

        private void FireUpdated()
        {
            try { DataUpdated?.Invoke(); } catch { }
        }

        // ----------------- symbols 持久化（%TEMP%） -----------------
        private static string SymbolsFile => Path.Combine(Path.GetTempPath(), "cadstockv2_symbols.txt");

        private void LoadSymbols(bool allowDefaultIfMissing)
        {
            try
            {
                var existed = File.Exists(SymbolsFile);

                _symbols.Clear();

                if (existed)
                {
                    // ✅ 允许用户用逗号/空格/换行随便写
                    var all = File.ReadAllText(SymbolsFile, Encoding.UTF8) ?? "";
                    var tokens = all.Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' },
                                           StringSplitOptions.RemoveEmptyEntries);

                    foreach (var t0 in tokens)
                    {
                        var s = NormalizeSymbol(t0);
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        if (!_symbols.Contains(s, StringComparer.OrdinalIgnoreCase))
                            _symbols.Add(s);
                    }

                    // ✅ 注意：文件存在但为空 => 就保持为空（不再强行塞默认值）
                    return;
                }

                // 文件不存在：第一次启动给默认值
                if (allowDefaultIfMissing)
                {
                    _symbols.AddRange(new[] { "sh600000", "sz000001", "sh600519" });
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
        public string Symbol { get; set; }      // sh600000 / sz000001 / sh000001...
        public string Name { get; set; }
        public decimal Price { get; set; }
        public decimal PrevClose { get; set; }

        // UI 用：不想显示前缀就用它
        public string SymbolShort
        {
            get
            {
                var s = Symbol ?? "";
                if (s.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
                    return s.Substring(2);
                return s;
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

        public override string ToString() => $"{Symbol} {Name} {Price}";
    }
}
