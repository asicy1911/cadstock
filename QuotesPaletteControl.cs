using System;
using System.Drawing;
using System.Windows.Forms;

namespace cadstockv2
{
    internal sealed class QuotesPaletteControl : UserControl
    {
        private readonly DataGridView _grid;
        private readonly Label _top;

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

            // ✅ 只保留：名称 / 涨跌幅
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Change", HeaderText = "涨跌幅" });

            Controls.Add(_grid);
            Controls.Add(_top);

            // 双击：只看这一只（可选）
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var sym = _grid.Rows[e.RowIndex].Tag as string;
                if (!string.IsNullOrWhiteSpace(sym))
                    PaletteHost.ApplySymbols(new[] { sym });
            };

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

            _top.Text = last == DateTime.MinValue
                ? "cadstock v2（未更新）"
                : $"cadstock v2（{last:HH:mm:ss}）";

            _grid.Rows.Clear();

            foreach (var q in list)
            {
                bool hasData = (q.PrevClose != 0m && q.Price != 0m);
                var cp = q.ChangePercent;

                string pctText = hasData
                    ? (cp >= 0 ? "+" : "") + cp.ToString("0.00") + "%"
                    : "--";

                int r = _grid.Rows.Add(q.Name, pctText);
                var row = _grid.Rows[r];
                row.Tag = q.Symbol;

                var color = !hasData ? Color.Gainsboro : (cp >= 0 ? Color.IndianRed : Color.MediumSeaGreen);
                row.Cells["Change"].Style.ForeColor = color;
            }
        }
    }
}
