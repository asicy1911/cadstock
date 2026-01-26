using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class DropdownCommands
    {
        [CommandMethod("CADSTOCKV2DROPDOWN")]
        public void CADSTOCKV2DROPDOWN()
        {
            StockQuoteService.Instance.Start();

            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };

            var list = StockQuoteService.Instance.GetSnapshot(out var lastUpdate);

            var header = new ToolStripMenuItem(
                $"更新时间：{(lastUpdate == DateTime.MinValue ? "无" : lastUpdate.ToString("HH:mm:ss"))}"
            )
            { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            if (list.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("暂无数据（稍等或检查网络）") { Enabled = false });
            }
            else
            {
                foreach (var q in list)
                {
                    // ✅ 下拉项显示：名称(代码)  价格
                    var text = $"{q.Name}({q.SymbolShort})   {q.Price:0.00}";
                    var item = new ToolStripMenuItem(text)
                    {
                        ForeColor = q.ChangePercent >= 0 ? Color.IndianRed : Color.MediumSeaGreen
                    };

                    item.Click += (s, e) =>
                    {
                        PaletteHost.ShowPalette();
                        PaletteHost.ApplySymbols(new[] { q.Symbol });
                    };

                    menu.Items.Add(item);
                }
            }

            // 更像从按钮弹出：稍微往下偏一点
            var p = Cursor.Position;
            p.Y += 6;
            menu.Show(p);
        }
    }
}
