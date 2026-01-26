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

            // ✅ 只保留：名称 / 代码 / 价格（不显示“涨跌”列）
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
                    StockQuoteService.Instance.SetSymbols(new[] { sym });
                }
            };

            SafeReloadFromService();
        }

        public void SetSymbols(string[] symbols)
        {
            _symbols = (symbols ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
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

            var list = StockQuoteService.Instance.GetSnapshot(out var last);

            if (_symbols.Count > 0)
            {
                // 按当前关注顺序排列
                var map = list.ToDictionary(x => x.Symbol, x => x);
                list = _symbols.Where(map.ContainsKey).Select(s => map[s]).ToList();
            }

            _top.Text = last == DateTime.MinValue
                ? "cadstock v2（未更新）"
                : $"cadstock v2（{last:HH:mm:ss}）";

            _grid.Rows.Clear();

            foreach (var q in list)
            {
                int r = _grid.Rows.Add(q.Name, q.SymbolShort, q.Price.ToString("0.00"));
                var row = _grid.Rows[r];
                row.Tag = q.Symbol;

                // 红涨绿跌：只在“价格”这一格着色（不显示涨跌列）
                var color = q.ChangePercent >= 0 ? Color.IndianRed : Color.MediumSeaGreen;
                row.Cells["Price"].Style.ForeColor = color;
            }
        }
    }
}
