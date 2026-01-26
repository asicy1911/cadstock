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

            // ✅ 只保留：名称 / 涨跌幅
            _grid.Columns.Clear();
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "名称",
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Chg",
                HeaderText = "涨跌幅",
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            Controls.Add(_grid);
            Controls.Add(_top);

            // 双击：把这只设为唯一关注（不想这样我再给你改）
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
            _symbols = (symbols ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
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

            var list = StockQuoteService.Instance.GetSnapshot();

            // 按当前关注顺序排列
            if (_symbols.Count > 0)
            {
                var map = list.ToDictionary(x => x.Symbol, x => x);
                list = _symbols.Where(map.ContainsKey).Select(s => map[s]).ToList();
            }

            var last = StockQuoteService.Instance.LastUpdate;
            _top.Text = last.HasValue
                ? $"cadstock v2（{last.Value:HH:mm:ss}）"
                : "cadstock v2（未更新）";

            _grid.Rows.Clear();

            foreach (var q in list)
            {
                string chgText;
                Color chgColor;
                GetChangeTextAndColor(q, out chgText, out chgColor);

                int r = _grid.Rows.Add(q.Name, chgText);
                var row = _grid.Rows[r];
                row.Tag = q.Symbol;

                // 只给“涨跌幅”着色
                row.Cells["Chg"].Style.ForeColor = chgColor;
            }
        }

        private static void GetChangeTextAndColor(StockQuote q, out string text, out Color color)
        {
            // 没有昨收/现价（或为 0）就视为未知
            if (q == null || q.PrevClose <= 0m || q.Price <= 0m)
            {
                text = "—";
                color = Color.Gainsboro;
                return;
            }

            // ChangePercent 是 decimal（不是 nullable）
            var cp = q.ChangePercent; // 1.23 表示 1.23%
            text = (cp >= 0 ? "+" : "") + cp.ToString("0.00") + "%";
            color = cp >= 0 ? Color.IndianRed : Color.MediumSeaGreen;
        }
    }
}
