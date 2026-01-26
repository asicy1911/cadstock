using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class DropdownCommands
    {
        [CommandMethod("CADSTOCKV2DROPDOWN", CommandFlags.Transparent | CommandFlags.NoHistory)]
        public void CADSTOCKV2DROPDOWN()
        {
            try
            {
                StockQuoteService.Instance.Start();
                StockQuoteService.Instance.ForceRefresh();

                var list = StockQuoteService.Instance.GetSnapshot();
                var menu = BuildMenu(list);

                // ✅ 直接用屏幕坐标弹出，不依赖 Autodesk.AutoCAD.Windows
                menu.Show(Cursor.Position);
            }
            catch
            {
                // 不写命令行，避免“跑到底部”
            }
        }

        private static ContextMenuStrip BuildMenu(System.Collections.Generic.List<StockQuote> list)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };

            var lu = StockQuoteService.Instance.LastUpdate;
            var err = StockQuoteService.Instance.LastError;

            string top;
            if (!string.IsNullOrWhiteSpace(err))
                top = "cadstock v2（错误）";
            else if (lu.HasValue)
                top = $"cadstock v2（{lu.Value:HH:mm:ss}）";
            else
                top = "cadstock v2（未更新）";

            menu.Items.Add(new ToolStripMenuItem(top) { Enabled = false });

            if (!string.IsNullOrWhiteSpace(err))
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem(err) { Enabled = false });
            }

            menu.Items.Add(new ToolStripSeparator());

            if (list == null || list.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("暂无数据") { Enabled = false });
                menu.Items.Add(new ToolStripSeparator());
            }
            else
            {
                foreach (var q in list.OrderBy(x => x.Symbol))
                {
                    var text = $"{q.Name}  {q.Symbol}  {q.Price:0.00}";
                    var item = new ToolStripMenuItem(text);

                    // ✅ 红涨绿跌（PrevClose/Price 有效才上色）
                    if (q.PrevClose != 0m && q.Price != 0m)
                        item.ForeColor = q.ChangePercent >= 0m ? Color.IndianRed : Color.MediumSeaGreen;
                    else
                        item.ForeColor = Color.Gainsboro;

                    var sym = q.Symbol;
                    item.Click += (s, e) =>
                    {
                        // 点击某条：设为关注并刷新（你想改成“打开面板”也行）
                        StockQuoteService.Instance.SetSymbols(new[] { sym });
                        StockQuoteService.Instance.ForceRefresh();
                    };

                    menu.Items.Add(item);
                }

                menu.Items.Add(new ToolStripSeparator());
            }

            var refresh = new ToolStripMenuItem("刷新");
            refresh.Click += (s, e) => StockQuoteService.Instance.ForceRefresh();
            menu.Items.Add(refresh);

            var openPanel = new ToolStripMenuItem("打开面板");
            openPanel.Click += (s, e) =>
            {
                // 这里按你现有命令名改：例如 Commands.cs 里是 CADSTOCKV2
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument
                    ?.SendStringToExecute("CADSTOCKV2 ", true, false, false);
            };
            menu.Items.Add(openPanel);

            return menu;
        }
    }
}
