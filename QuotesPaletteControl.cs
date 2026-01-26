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

namespace cadstock
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
            _http.Timeout = TimeSpan.FromSeconds(4);

            _timer.Interval = _intervalMs;
            _timer.Tick += async (s, e) => await RefreshAsync();
        }

        public void Start()
        {
            _timer.Start();
            _ = RefreshAsync();
        }

        public void Stop() => _timer.Stop();

        private void BuildUi()
        {
            BackColor = Color.Black;

            _status.Dock = DockStyle.Top;
            _status.Height = 26;
            _status.ForeColor = Color.Gainsboro;
            _status.BackColor = Color.Black;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(8, 0, 8, 0);
            _status.Text = "cadstock：加载中…";

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

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "指数", FillWeight = 42 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Last", HeaderText = "点位", FillWeight = 23 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Chg", HeaderText = "涨跌", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pct", HeaderText = "涨跌幅", FillWeight = 17 });

            var menu = new ContextMenuStrip();
            menu.Items.Add("设置刷新间隔(秒)…", null, (s, e) => SetInterval());
            menu.Items.Add("设置指数列表…", null, (s, e) => SetSymbols());
            menu.Items.Add("立即刷新", null, async (s, e) => await RefreshAsync());
            _grid.ContextMenuStrip = menu;

            Controls.Add(_grid);
            Controls.Add(_status);
        }

        private void SetInterval()
        {
            var input = Prompt("输入刷新间隔（秒），建议 1~10：", (_intervalMs / 1000.0).ToString(CultureInfo.InvariantCulture));
            if (input == null) return;

            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
            {
                sec = Math.Max(0.5, Math.Min(60, sec));
                _intervalMs = (int)(sec * 1000);
                _timer.Interval = _intervalMs;
            }
        }

        private void SetSymbols()
        {
            var input = Prompt("输入指数代码（逗号分隔），例如：s_sh000001,s_sz399001", string.Join(",", _symbols));
            if (input == null) return;

            var list = input.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => x.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

            if (list.Count > 0) _symbols = list;
        }

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;

            try
            {
                var quotes = await FetchSinaIndexAsync(_symbols);
                Render(quotes);
                _status.Text = $"cadstock：{DateTime.Now:HH:mm:ss} 刷新完成（{_symbols.Count}）  右键可设置";
            }
            catch (Exception ex)
            {
                _status.Text = $"cadstock：刷新失败 {DateTime.Now:HH:mm:ss}  {ex.Message}";
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task<List<Quote>> FetchSinaIndexAsync(IReadOnlyList<string> symbols)
        {
            if (symbols == null || symbols.Count == 0) return new List<Quote>();

            var url = "https://hq.sinajs.cn/list=" + string.Join(",", symbols);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var text = Encoding.GetEncoding("GB2312").GetString(bytes);

            var list = new List<Quote>();
            var rx = new Regex(@"var\s+hq_str_(?<sym>[^=]+)=""(?<data>[^""]*)"";", RegexOptions.Compiled);

            foreach (Match m in rx.Matches(text))
            {
                var code = m.Groups["sym"].Value.Trim();
                var data = m.Groups["data"].Value.Trim();
                if (string.IsNullOrWhiteSpace(data)) continue;

                var parts = data.Split(',').Select(x => x.Trim()).ToArray();
                if (parts.Length < 4) continue;

                list.Add(new Quote
                {
                    Symbol = code,
                    Name = parts[0],
                    Last = ParseDecimal(parts.ElementAtOrDefault(1)),
                    Change = ParseDecimal(parts.ElementAtOrDefault(2)),
                    ChangePct = ParseDecimal(parts.ElementAtOrDefault(3)),
                });
            }

            var order = symbols.Select((s, i) => new { s, i })
                               .ToDictionary(x => x.s, x => x.i, StringComparer.OrdinalIgnoreCase);
            return list.OrderBy(x => order.TryGetValue(x.Symbol, out var i) ? i : int.MaxValue).ToList();
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

            // 你最爱的配色在这里改：
            var up = Color.IndianRed;    // 涨
            var down = Color.LimeGreen;  // 跌
            var flat = Color.Gainsboro;  // 平

            foreach (var q in quotes)
            {
                var idx = _grid.Rows.Add(
                    q.Name,
                    q.Last.ToString("0.###"),
                    q.Change.ToString("0.###"),
                    q.ChangePct.ToString("0.##") + "%");

                var row = _grid.Rows[idx];
                var c = q.Change > 0 ? up : (q.Change < 0 ? down : flat);

                row.Cells["Chg"].Style.ForeColor = c;
                row.Cells["Pct"].Style.ForeColor = c;

                row.Cells["Last"].Style.ForeColor = Color.White;
                row.Cells["Name"].Style.ForeColor = Color.Gainsboro;
                row.Height = 26;
            }

            _grid.ResumeLayout();
        }

        private static string Prompt(string title, string defaultValue)
        {
            using var f = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(520, 140),
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.Gainsboro
            };

            var tb = new TextBox
            {
                Left = 12,
                Top = 16,
                Width = 496,
                Text = defaultValue ?? "",
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 340, Width = 80, Top = 70 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 428, Width = 80, Top = 70 };

            f.Controls.Add(tb);
            f.Controls.Add(ok);
            f.Controls.Add(cancel);
            f.AcceptButton = ok;
            f.CancelButton = cancel;

            return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }

        private sealed class Quote
        {
            public string Symbol { get; set; }
            public string Name { get; set; }
            public decimal Last { get; set; }
            public decimal Change { get; set; }
            public decimal ChangePct { get; set; }
        }
    }
}
