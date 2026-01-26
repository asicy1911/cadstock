using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace cadstockv2
{
    internal sealed class QuotesPaletteControl : UserControl
    {
        private readonly DataGridView _grid;
        private readonly Label _top;
        private List<string> _symbols = new List<string>();

        public QuotesPaletteControl()
        {
            BackColor = Color.Black;

            _top = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Black,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "cadstock v2"
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.Black,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(40, 40, 40),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.Black;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;
            _grid.EnableHeadersVisualStyles = false;

            _grid.DefaultCellStyle.BackColor = Color.Black;
            _grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 30, 30);
            _grid.DefaultCellStyle.SelectionForeColor = Color.Gainsboro;

            // ✅ 只保留：名称 / 代码 / 价格
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "代码" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "价格" });

            Controls.Add(_grid);
            Controls.Add(_top);

            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var sym = _grid.Rows[e.RowIndex].Tag as string;
                if (!string.IsNullOrWhiteSpace(sym))
                {
                    // 你原来这里逻辑像“设为单只关注”，我保留
                    StockQuoteService.Instance.SetSymbols(new[] { sym });
                }
            };

            SafeReloadFromService();
        }

        public void SetSymbols(string[] symbols)
        {
            _symbols = (symbols ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            SafeReloadFromService();
        }

        public void SafeReloadFromService()
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(SafeReloadFromService));
                return;
            }

            // ✅ 新：直接拿快照 + 用 LastUpdate (DateTime?)
            var svc = StockQuoteService.Instance;
            var list = svc.GetSnapshot();
            var last = svc.LastUpdate;

            if (_symbols.Count > 0)
            {
                // 按当前关注顺序排列（不存在的过滤掉）
                var map = list.ToDictionary(x => x.Symbol, x => x, StringComparer.OrdinalIgnoreCase);
                list = _symbols.Where(map.ContainsKey).Select(s => map[s]).ToList();
            }

            _top.Text = last.HasValue
                ? $"cadstock v2（{last.Value:HH:mm:ss}）"
                : "cadstock v2（未更新）";

            _grid.Rows.Clear();

            foreach (var q in list)
            {
                // ✅ Code：用 Symbol（不再用 SymbolShort）
                int r = _grid.Rows.Add(q.Name, q.Symbol, q.Price.ToString("0.00"));
                var row = _grid.Rows[r];
                row.Tag = q.Symbol;

                // ✅ 红涨绿跌：需要 ChangePercent；如果你 StockQuote 里没有，就先按“未知=灰色”
                var cp = q.ChangePercent; // 下面我会告诉你 StockQuoteService 里要提供它
                Color color;
                if (cp.HasValue)
                    color = cp.Value >= 0 ? Color.IndianRed : Color.MediumSeaGreen;
                else
                    color = Color.Gainsboro;

                row.Cells["Price"].Style.ForeColor = color;
            }
        }
    }
}
