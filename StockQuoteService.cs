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
                    // 立即跑一次，然后每 10 秒刷新（你也可以改成 5 秒）
                    _timer = new Timer(async _ => await TickSafeAsync().ConfigureAwait(false), null, 0, 10_000);
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

        /// <summary>强制立刻刷新一次（给 Commands.cs 用）</summary>
        public void ForceRefresh()
        {
            // 不阻塞 UI，异步跑一轮
            Task.Run(async () => await TickSafeAsync().ConfigureAwait(false));
        }

        /// <summary>你下拉/面板需要的列表</summary>
        public List<StockQuote> GetSnapshot()
        {
            lock (_lock)
            {
                return _latest.Values
                    .OrderBy(q => q.Symbol)
                    .ToList();
            }
        }

        /// <summary>取单只（给 Dropdown/Palette 可能会用）</summary>
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

                SaveSymbols();
            }

            ForceRefresh();
        }

        private async Task TickSafeAsync()
        {
            // 防止 Timer 重入（上一次没跑完又进来）
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1) return;

            try
            {
                await TickAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
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
            // 注意：别太大，避免被限流。这里一次最多 50 个。
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

                    // Sina A股接口经常是 GBK/GB2312
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    text = DecodeSina(bytes);
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    LastError = ex.GetType().Name + ": " + ex.Message;
                    LastUpdate = null;
                }
                FireUpdated();
                return;
            }

            // 解析每行：var hq_str_sh600000="xxx,xxx,...";
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
                // net46 一般自带 936（GBK）
                return Encoding.GetEncoding(936).GetString(bytes);
            }
            catch
            {
                // 兜底
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

            // 按行切
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                // 例：var hq_str_sh600000="浦发银行,10.930,10.920,10.900,10.980,10.890,...,2026-01-26,15:00:00,00";
                var line = raw.Trim();
                if (!line.StartsWith("var hq_str_", StringComparison.OrdinalIgnoreCase)) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                // key：hq_str_sh600000
                var key = line.Substring("var ".Length, eq - "var ".Length).Trim(); // hq_str_sh600000
                var code = key.Replace("hq_str_", "").Trim(); // sh600000

                // value："..."
                int q1 = line.IndexOf('"', eq);
                if (q1 < 0) continue;
                int q2 = line.LastIndexOf('"');
                if (q2 <= q1) continue;

                var payload = line.Substring(q1 + 1, q2 - q1 - 1);
                if (string.IsNullOrWhiteSpace(payload))
                    continue; // 有些 code 不存在会返回空串

                var fields = payload.Split(',');
                // 常见 A 股字段：
                // 0 名称
                // 1 今开
                // 2 昨收
                // 3 现价
                // ... 最后一般是 日期、时间（A股通常 fields[30]=date, [31]=time）
                if (fields.Length < 4) continue;

                var name = fields[0];
                decimal prev = ParseDecimal(fields, 2);
                decimal price = ParseDecimal(fields, 3);

                // code -> 6位数字 symbol
                var symbol = NormalizeSymbol(code);

                // 如果 price 为 0，可能停牌/非交易时段/返回异常，仍然保留但不算 ok
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
                    // 记录部分原文便于你调试（不要太长）
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

            // 允许用户输入：sh600000 / sz000001 / SH600000 等
            if (s.Length >= 2)
            {
                var p = s.Substring(0, 2);
                if (string.Equals(p, "sh", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "sz", StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(2);
                }
            }

            // 允许用户输入带点的 1.600000 / 0.000001（之前东财 secid）
            if (s.Length > 2 && s[1] == '.' && (s[0] == '0' || s[0] == '1'))
                s = s.Substring(2);

            return s;
        }

        private static string ToSinaCode(string symbol)
        {
            symbol = NormalizeSymbol(symbol);

            // 上交所：6/5/9 开头常见（股票/基金/等）
            // 深交所：0/3 开头常见
            if (symbol.StartsWith("6") || symbol.StartsWith("5") || symbol.StartsWith("9"))
                return "sh" + symbol;

            return "sz" + symbol;
        }

        private void FireUpdated()
        {
            try { DataUpdated?.Invoke(); } catch { }
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

                // 默认给几个，避免你第一次就“暂无数据”
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

        // 给你其它文件可能会用到的字段（之前报过缺成员）
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
