using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

namespace cadstockv2
{
    public class DropdownCommands
    {
        // 这个名字要和你工具栏宏里调用的一致：_CADSTOCKV2DROPDOWN
        [CommandMethod("CADSTOCKV2DROPDOWN", CommandFlags.Transparent | CommandFlags.NoHistory)]
        public void CADSTOCKV2DROPDOWN()
        {
            try
            {
                // 确保服务在跑
                StockQuoteService.Instance.Start();
                StockQuoteService.Instance.ForceRefresh();

                // 拿快照
                var list = StockQuoteService.Instance.GetSnapshot();

                // 动态菜单
                var menu = BuildMenu(list);

                // 在鼠标位置弹出
                ShowMenuAtCursor(menu);
            }
            catch
            {
                // 不往命令行打印，避免你说的“显示在底部”
                // 需要调试时你可以临时加 Editor.WriteMessage
            }
        }

        private static ContextMenuStrip BuildMenu(System.Collections.Generic.List<StockQuote> list)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };

            // 顶部：更新时间/错误
            var lu = StockQuoteService.Instance.LastUpdate;
            var err = StockQuoteService.Instance.LastError;

            string top;
            if (!string.IsNullOrWhiteSpace(err))
                top = "cadstock v2（错误）";
            else if (lu.HasValue)
                top = $"cadstock v2（{lu.Value:HH:mm:ss}）";
            else
                top = "cadstock v2（未更新）";

            var header = new ToolStripMenuItem(top) { Enabled = false };
            menu.Items.Add(header);

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

                    // 红涨绿跌：PrevClose/Price 有效才上色，否则灰
                    if (q.PrevClose != 0m && q.Price != 0m)
                        item.ForeColor = q.ChangePercent >= 0m ? Color.IndianRed : Color.MediumSeaGreen;
                    else
                        item.ForeColor = Color.Gainsboro;

                    var sym = q.Symbol;
                    item.Click += (s, e) =>
                    {
                        // 你这里按你想要的行为改：
                        // 1) 只把它设为关注列表并刷新
                        StockQuoteService.Instance.SetSymbols(new[] { sym });
                        StockQuoteService.Instance.ForceRefresh();

                        // 2) 或者打开面板（如果你有命令/方法）
                        // Commands.CADSTOCKV2SHOW(); // 示例
                    };

                    menu.Items.Add(item);
                }

                menu.Items.Add(new ToolStripSeparator());
            }

            // 常用操作
            var refresh = new ToolStripMenuItem("刷新")
            {
                ShortcutKeyDisplayString = "F5"
            };
            refresh.Click += (s, e) => StockQuoteService.Instance.ForceRefresh();
            menu.Items.Add(refresh);

            var openPanel = new ToolStripMenuItem("打开面板");
            openPanel.Click += (s, e) =>
            {
                // 如果你 Commands.cs 里有显示面板命令，改成对应命令名
                // 例如：CADSTOCKV2
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument
                    ?.SendStringToExecute("CADSTOCKV2 ", true, false, false);
            };
            menu.Items.Add(openPanel);

            return menu;
        }

        private static void ShowMenuAtCursor(ContextMenuStrip menu)
        {
            // 用 AutoCAD 主窗口作为 owner，避免菜单跑到后面/焦点问题
            var hwnd = ComponentManager.ApplicationWindow;
            var owner = new WindowWrapper(hwnd);

            var screenPt = Cursor.Position;
            // ContextMenuStrip.Show(IWin32Window, Point) 这里的 Point 是 owner 客户区坐标
            // 但 WindowWrapper 没有 PointToClient，所以直接用 Show(Point)（screen coords）更稳
            // menu.Show(owner, owner.PointToClient(screenPt)); // 如果你有 Form 才能这样
            menu.Show(screenPt);
        }
    }
}
