using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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

        public DateTime? LastUpdate { get; private set; }
        public string LastError { get; private set; }

        public event Action DataUpdated;

        private StockQuoteService()
        {
            // ✅ 强制开启 TLS1.2（很多 HTTPS 行情接口都要求）
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
                    _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) cadstockv2");
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

        public void SetSymbols(IEnumerable<string> symbols)
        {
            lock (_lock)
            {
                _symbols.Clear();
                _symbols.AddRange(symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeSymbol).Distinct());
                SaveSymbols();
            }
        }

        private async Task TickSafeAsync()
        {
            try
            {
                await TickAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    LastError = ex.GetType().Name + ": " + ex.Message;
                }
                FireUpdated();
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

            // 并发拉取（别太大，避免被限流）
            var tasks = list.Take(30).Select(s => FetchOneAsync(s)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            int ok = 0;
            lock (_lock)
            {
                foreach (var q in results)
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
                    // 没有任何成功时，保留 LastError（FetchOneAsync 里会写）
                    if (string.IsNullOrWhiteSpace(LastError))
                        LastError = "全部请求失败/解析失败（ok=0）";
                    // 不更新 LastUpdate，让你看到“更新时间无”
                    LastUpdate = null;
                }
            }

            FireUpdated();
        }

        private async Task<StockQuote> FetchOneAsync(string symbol)
        {
            symbol = NormalizeSymbol(symbol);
            var secid = ToSecId(symbol); // 6开头 => 1.xxxxxx  否则 0.xxxxxx :contentReference[oaicite:1]{index=1}
            var url = "https://push2.eastmoney.com/api/qt/stock/get?secid=" + secid + "&fields=f58,f43,f60";

            try
            {
                using (var resp = await _http.GetAsync(url).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        lock (_lock) LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                        return null;
                    }

                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var data = Deserialize<EastMoneyResp>(bytes);
                    if (data?.Data == null)
                    {
                        lock (_lock) LastError = "JSON 解析成功但 data==null（接口可能返回结构变化/被拦截）";
                        return null;
                    }

                    // f43 现价，f60 昨收，f58 名称 :contentReference[oaicite:2]{index=2}
                    var name = data.Data.f58 ?? symbol;
                    var price = data.Data.f43 / 100.0m;
                    var prev = data.Data.f60 / 100.0m;

                    return new StockQuote
                    {
                        Symbol = symbol,
                        Name = name,
                        Price = price,
                        PrevClose = prev
                    };
                }
            }
            catch (Exception ex)
            {
                lock (_lock) LastError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private static T Deserialize<T>(byte[] jsonBytes) where T : class
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(jsonBytes))
            {
                return ser.ReadObject(ms) as T;
            }
        }

        private static string NormalizeSymbol(string s)
        {
            s = (s ?? "").Trim();
            // 允许用户输入带前缀的 sh600000 / sz000001，统一成 6位数字
            s = s.Replace("sh", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("sz", "", StringComparison.OrdinalIgnoreCase);
            return s;
        }

        private static string ToSecId(string symbol)
        {
            if (symbol.StartsWith("6")) return "1." + symbol;   // 上交所
            return "0." + symbol;                                // 深交所/创业板等
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
                        .Distinct()
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

        public override string ToString()
            => $"{Symbol} {Name} {Price}";
    }

    [DataContract]
    internal sealed class EastMoneyResp
    {
        [DataMember(Name = "data")]
        public EastMoneyData Data { get; set; }
    }

    [DataContract]
    internal sealed class EastMoneyData
    {
        [DataMember(Name = "f58")]
        public string f58 { get; set; } // 名称

        [DataMember(Name = "f43")]
        public long f43 { get; set; } // 现价 * 100

        [DataMember(Name = "f60")]
        public long f60 { get; set; } // 昨收 * 100
    }
}
