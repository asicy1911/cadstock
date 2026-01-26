using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private readonly List<string> _symbols = new List<string>
        {
            // 默认关注（你可改）
            "s_sh600519",
            "s_sh601318",
            "s_sh600036",
            "s_sz000001",
            "s_sz300750",
        };

        private readonly List<Quote> _snapshot = new List<Quote>();
        private DateTime _lastUpdate = DateTime.MinValue;

        private Timer _timer;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);

        private volatile bool _started;

        private StockQuoteService() { }

        public void Start()
        {
            if (_started) return;
            _started = true;

            _timer?.Dispose();
            _timer = new Timer(async _ => await SafeRefreshAsync().ConfigureAwait(false), null, 0, 5000);
        }

        public void Stop()
        {
            _started = false;
            _timer?.Dispose();
            _timer = null;
        }

        public void SetSymbols(IEnumerable<string> symbols)
        {
            if (symbols == null) return;
            lock (_lock)
            {
                _symbols.Clear();
                _symbols.AddRange(symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            }
            ForceRefresh();
        }

        public void ForceRefresh()
        {
            _ = SafeRefreshAsync();
        }

        public List<Quote> GetSnapshot(out DateTime lastUpdate)
        {
            lock (_lock)
            {
                lastUpdate = _lastUpdate;
                return _snapshot.Select(x => x.Clone()).ToList();
            }
        }

        private async Task SafeRefreshAsync()
        {
            if (!_started) return;
            if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false)) return;

            try
            {
                await RefreshAsync().ConfigureAwait(false);
            }
            catch
            {
                // 不抛给 CAD，静默失败即可（菜单会显示暂无数据或旧数据）
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private async Task RefreshAsync()
        {
            List<string> syms;
            lock (_lock) syms = _symbols.ToList();
            if (syms.Count == 0) return;

            // Sina 行情：返回 GBK，形如 var hq_str_s_sh600519="..."
            var url = "https://hq.sinajs.cn/list=" + string.Join(",", syms);

            byte[] bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            var text = Encoding.GetEncoding("GBK").GetString(bytes);

            var parsed = QuoteParser.ParseSinaMini(text, syms);

            lock (_lock)
            {
                _snapshot.Clear();
                _snapshot.AddRange(parsed);
                _lastUpdate = DateTime.Now;
            }

            // 通知面板刷新
            PaletteHost.NotifyQuotesUpdated();
        }
    }

    internal sealed class Quote
    {
        public string Symbol { get; set; }          // s_sh600519
        public string SymbolShort { get; set; }     // 600519
        public string Name { get; set; }            // 贵州茅台
        public decimal Price { get; set; }          // 当前价
        public decimal ChangePercent { get; set; }  // 涨跌幅（用于颜色，不展示列）

        public Quote Clone() => new Quote
        {
            Symbol = Symbol,
            SymbolShort = SymbolShort,
            Name = Name,
            Price = Price,
            ChangePercent = ChangePercent
        };
    }

    internal static class QuoteParser
    {
        public static List<Quote> ParseSinaMini(string text, List<string> symbols)
        {
            // s_ 开头的简版字段：name,price,chg,chgPercent,volume,amount
            // 例：var hq_str_s_sh600519="贵州茅台,1600.00,5.00,0.31,12345,123456789";
            var list = new List<Quote>();

            foreach (var sym in symbols)
            {
                var key = $"var hq_str_{sym}=\"";
                var idx = text.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) continue;

                idx += key.Length;
                var end = text.IndexOf("\";", idx, StringComparison.Ordinal);
                if (end < 0) continue;

                var payload = text.Substring(idx, end - idx);
                if (string.IsNullOrWhiteSpace(payload)) continue;

                var parts = payload.Split(',');
                if (parts.Length < 4) continue;

                var name = parts[0]?.Trim();
                var price = ParseDec(parts[1]);
                var chgPct = ParseDec(parts[3]);

                list.Add(new Quote
                {
                    Symbol = sym,
                    SymbolShort = ToShort(sym),
                    Name = string.IsNullOrWhiteSpace(name) ? ToShort(sym) : name,
                    Price = price,
                    ChangePercent = chgPct
                });
            }

            // 按涨幅排序（你也可以按代码排序）
            return list.OrderByDescending(x => x.ChangePercent).ToList();
        }

        private static decimal ParseDec(string s)
        {
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v;
            return 0m;
        }

        private static string ToShort(string sym)
        {
            // s_sh600519 -> 600519
            if (string.IsNullOrWhiteSpace(sym)) return sym;
            var p = sym.LastIndexOf("sh", StringComparison.OrdinalIgnoreCase);
            if (p >= 0 && sym.Length >= p + 2 + 6) return sym.Substring(p + 2, 6);
            p = sym.LastIndexOf("sz", StringComparison.OrdinalIgnoreCase);
            if (p >= 0 && sym.Length >= p + 2 + 6) return sym.Substring(p + 2, 6);
            return sym;
        }
    }
}
