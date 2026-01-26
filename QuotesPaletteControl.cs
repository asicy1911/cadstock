using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace cadstockv2
{
    internal sealed class QuotesPaletteControl : UserControl
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _status = new Label();
        private readonly Timer _timer = new Timer();
        private readonly HttpClient _http = new HttpClient();

        private List<string> _symbols = new List<string>
        {
            "s_sh000001",
            "s_sz399001",
            "s_sz399006",
            "s_sh000300",
        };

        private int _intervalMs = 2000;
        private bool _busy;

        public QuotesPaletteControl()
        {
            BuildUi();

            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            _http.Timeout = TimeSpan.FromSeconds(12);

            _timer.Interval = _intervalMs;
            _timer.Tick += async (s, e) => await RefreshAsync();
        }

        public void Start()
        {
            _timer.Start();
            _ = RefreshAsync();
        }

        public void Stop() => _timer.Stop();

        public void RefreshNow() => _ = RefreshAsync();

        public void SetSymbols(IEnumerable<string> symbols)
        {
            var list = (symbols ?? Array.Empty<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count > 0) _symbols = list;
        }

        private void BuildUi()
        {
            BackColor = Color.Black;

            _status.Dock = DockStyle.Top;
            _status.Height = 26;
            _status.ForeColor = Color.Gainsboro;
            _status.BackColor = Color.Black;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(8, 0, 8, 0);
            _status.Text = "cadstock v2：加载中…";

            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Color.Black;
            _grid.BorderStyle = BorderStyle.None;
            _grid.RowHeadersVisible = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.EnableHeadersVisualStyles = false;

            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(20, 20, 20);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;

            _grid.DefaultCellStyle.BackColor = Color.Black;
            _grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(35, 35, 35);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;

            _grid.GridColor = Color.FromArgb(30, 30, 30);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "指数", FillWeight = 50 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Last", HeaderText = "点位", FillWeight = 28 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pct", HeaderText = "涨跌幅", FillWeight = 22 });

            Controls.Add(_grid);
            Controls.Add(_status);
        }

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;

            try
            {
                var quotes = await FetchSinaIndexAsync(_symbols);
                Render(quotes);
                _status.Text = $"cadstock v2：{DateTime.Now:HH:mm:ss} 刷新完成（{_symbols.Count}）";
            }
            catch (TaskCanceledException)
            {
                _status.Text = $"cadstock v2：刷新超时 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                _status.Text = $"cadstock v2：刷新失败 {DateTime.Now:HH:mm:ss}  {ex.Message}";
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task<List<Quote>> FetchSinaIndexAsync(IReadOnlyList<string> symbols)
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

            var list = new List<Quote>();
            var rx = new Regex(@"var\s+hq_str_(?<sym>[^=]+)=""(?<data>[^""]*)"";", RegexOptions.Compiled);

            foreach (Match m in rx.Matches(text))
            {
                var data = m.Groups["data"].Value.Trim();
                if (string.IsNullOrWhiteSpace(data)) continue;

                var parts = data.Split(',').Select(x => x.Trim()).ToArray();
                if (parts.Length < 4) continue;

                list.Add(new Quote
                {
                    Name = parts[0],
                    Last = ParseDecimal(parts.ElementAtOrDefault(1)),
                    ChangePct = ParseDecimal(parts.ElementAtOrDefault(3)),
                });
            }

            return list;
        }

        private static decimal ParseDecimal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace("%", "").Trim();
            decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        private void Render(List<Quote> quotes)
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear();

            var up = Color.IndianRed;    // 涨
            var down = Color.LimeGreen;  // 跌
            var flat = Color.Gainsboro;  // 平

            foreach (var q in quotes)
            {
                var idx = _grid.Rows.Add(
                    q.Name,
                    q.Last.ToString("0.###"),
                    q.ChangePct.ToString("0.##") + "%");

                var row = _grid.Rows[idx];
                var c = q.ChangePct > 0 ? up : (q.ChangePct < 0 ? down : flat);

                row.Cells["Pct"].Style.ForeColor = c;
                row.Cells["Last"].Style.ForeColor = Color.White;
                row.Cells["Name"].Style.ForeColor = Color.Gainsboro;
                row.Height = 26;
            }

            _grid.ResumeLayout();
        }

        private sealed class Quote
        {
            public string Name { get; set; }
            public decimal Last { get; set; }
            public decimal ChangePct { get; set; }
        }
    }
}
