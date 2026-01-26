using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace cadstockv2
{
    internal sealed class StockQuoteService
    {
        public static StockQuoteService Instance { get; } = new StockQuoteService();

        private readonly HttpClient _http = new HttpClient();
        private readonly object _lock = new object();
        private List<Quote> _latest = new List<Quote>();
        private DateTime _lastUpdate = DateTime.MinValue;

        // 你可以改成自己的关注列表（必须用 s_ 简版格式）
        private readonly List<string> _watch = new List<string>
        {
            "s_sh600519", // 贵州茅台
            "s_sh601318", // 中国平安
            "s_sh600036", // 招商银行
            "s_sh600000", // 浦发银行
            "s_sz000001", // 平安银行
            "s_sz300750", // 宁德时代
            "s_sz002594", // 比亚迪
            "s_sh600941", // 中国移动(A股)
        };

        private int _intervalMs = 5000;
        private Timer _timer;
        private int _refreshing;

        private StockQuoteService()
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(async _ => await RefreshAsync(), null, 0, _intervalMs);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public IReadOnlyList<Quote> GetSnapshot(out DateTime lastUpdate)
        {
            lock (_lock)
            {
                lastUpdate = _lastUpdate;
                return _latest.ToList();
            }
        }

        public void SetWatchlist(IEnumerable<string> symbols)
        {
            var list = (symbols ?? Array.Empty<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count == 0) return;

            lock (_lock)
            {
                _watch.Clear();
                _watch.AddRange(list);
            }

            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            // 防止重入
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

            try
            {
                List<string> symbols;
                lock (_lock) symbols = _watch.ToList();

                var quotes = await FetchSinaSimpleAsync(symbols);

                lock (_lock)
                {
                    _latest = quotes;
                    _lastUpdate = DateTime.Now;
                }
            }
            catch
            {
                // 下拉菜单里显示“上次数据”，这里静默即可
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private async Task<List<Quote>> FetchSinaSimpleAsync(IReadOnlyList<string> symbols)
        {
            if (symbols == null || symbols.Count == 0) return new List<Quote>();

            var qs = string.Join(",", symbols);
            var urls = new[]
            {
                "https://hq.sinajs.cn/list=" + qs,
                "http://hq.sinajs.cn/list=" + qs
            };

            HttpResponseMessage resp = null;
            Exception last = null;

            foreach (var url in urls)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");
                    resp = await _http.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    resp?.Dispose();
                    resp = null;
                }
            }

            if (last != null) throw last;

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var text = Encoding.GetEncoding("GB2312").GetString(bytes);

            // s_简版：name, price, chg, pct, volume, amount
            var rx = new Regex(@"var\s+hq_str_(?<sym>[^=]+)=""(?<data>[^""]*)"";", RegexOptions.Compiled);

            var list = new List<Quote>();

            foreach (Match m in rx.Matches(text))
            {
                var sym = m.Groups["sym"].Value.Trim(); // e.g. s_sh600519
                var data = m.Groups["data"].Value.Trim();
                if (string.IsNullOrWhiteSpace(data)) continue;

                var parts = data.Split(',').Select(x => x.Trim()).ToArray();
                if (parts.Length < 4) continue;

                var name = parts[0];
                var price = ParseDecimal(parts[1]);
                var chg = ParseDecimal(parts[2]);
                var pct = ParseDecimal(parts[3]);

                list.Add(new Quote
                {
                    Symbol = sym,
                    Name = name,
                    Price = price,
                    Change = chg,
                    ChangePct = pct
                });
            }

            // 保持 watchlist 顺序
            var order = symbols.Select((s, i) => new { s, i })
                .ToDictionary(x => x.s, x => x.i, StringComparer.OrdinalIgnoreCase);

            return list.OrderBy(q => order.TryGetValue(q.Symbol, out var i) ? i : int.MaxValue).ToList();
        }

        private static decimal ParseDecimal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace("%", "").Trim();
            decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        internal sealed class Quote
        {
            public string Symbol { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
            public decimal Change { get; set; }
            public decimal ChangePct { get; set; }

            public Color GetColor()
            {
                if (ChangePct > 0) return Color.IndianRed;
                if (ChangePct < 0) return Color.LimeGreen;
                return Color.Gainsboro;
            }

            public string ToMenuText()
            {
                // 例：贵州茅台 1688.00 +1.23%
                var sign = ChangePct > 0 ? "+" : "";
                return $"{Name}  {Price:0.##}  {sign}{ChangePct:0.##}%";
            }
        }
    }
}
