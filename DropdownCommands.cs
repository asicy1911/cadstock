using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace cadstockv2
{
    /// <summary>
    /// 点击工具栏按钮触发的下拉菜单（用 AutoCAD 命令实现）
    /// </summary>
    public class DropdownCommands
    {
        // 这个命令名要和 ClassicToolbarInstall 里写的一致： "_CADSTOCKV2DROPDOWN "
        [CommandMethod("CADSTOCKV2DROPDOWN")]
        public void ShowDropdown()
        {
            try
            {
                // 尽量保证服务在跑
                StockQuoteService.Instance.Start();

                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                if (ed == null) return;

                // 用 WinForms ContextMenuStrip 做菜单
                using (var menu = BuildMenu(ed))
                {
                    // 在鼠标位置弹出（WinForms 屏幕坐标）
                    var p = Control.MousePosition;
                    menu.Show(p);
                }
            }
            catch (System.Exception ex)
            {
                try
                {
                    var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                    ed?.WriteMessage("\n[cadstockv2] Dropdown failed: " + ex.GetType().Name + " - " + ex.Message);
                }
                catch { }
            }
        }

        private static ContextMenuStrip BuildMenu(Editor ed)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                RenderMode = ToolStripRenderMode.Professional,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };

            // ✅ 黑色主题渲染（仅改 BackColor 有时会被系统主题覆盖）
            menu.Renderer = new BlackMenuRenderer();

            // 顶部标题/状态
            var last = StockQuoteService.Instance.LastUpdate;
            var err = StockQuoteService.Instance.LastError;

            var title = new ToolStripMenuItem("cadstock v2")
            {
                Enabled = false,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };
            menu.Items.Add(title);

            string status;
            if (last.HasValue)
                status = "更新时间: " + last.Value.ToString("HH:mm:ss");
            else if (!string.IsNullOrWhiteSpace(err))
                status = "未更新: " + err;
            else
                status = "未更新";

            var stat = new ToolStripMenuItem(status)
            {
                Enabled = false,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };
            menu.Items.Add(stat);

            menu.Items.Add(new ToolStripSeparator());

            // 行情列表
            var list = StockQuoteService.Instance.GetSnapshot();

            if (list == null || list.Count == 0)
            {
                var empty = new ToolStripMenuItem("暂无数据（稍等或点“立即刷新”）")
                {
                    Enabled = false,
                    BackColor = Color.Black,
                    ForeColor = Color.Gainsboro
                };
                menu.Items.Add(empty);
            }
            else
            {
                // 最多显示 25 条，避免菜单太长
                foreach (var q in list.Take(25))
                {
                    var cp = q.ChangePercent; // decimal
                    var color = cp >= 0 ? Color.IndianRed : Color.MediumSeaGreen;

                    // 一行：名称  代码  价格
                    var text = $"{q.Name}  {q.Symbol}  {q.Price:0.00}";
                    var item = new ToolStripMenuItem(text)
                    {
                        BackColor = Color.Black,
                        ForeColor = color
                    };

                    // 点击：切换关注为“只看这一只”，并立刻刷新（你想改成“加入关注”也行）
                    var sym = q.Symbol;
                    item.Click += (s, e) =>
                    {
                        try
                        {
                            StockQuoteService.Instance.SetSymbols(new[] { sym });
                            StockQuoteService.Instance.ForceRefresh();
                            ed.WriteMessage($"\n[cadstockv2] Focus: {sym}");
                        }
                        catch { }
                    };

                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new ToolStripSeparator());

            // 立即刷新
            var refresh = new ToolStripMenuItem("立即刷新")
            {
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };
            refresh.Click += (s, e) =>
            {
                try
                {
                    StockQuoteService.Instance.ForceRefresh();
                    ed.WriteMessage("\n[cadstockv2] Refresh requested.");
                }
                catch { }
            };
            menu.Items.Add(refresh);

            // 打开面板（如果你有面板命令）
            var openPanel = new ToolStripMenuItem("打开面板")
            {
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };
            openPanel.Click += (s, e) =>
            {
                try
                {
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("CADSTOCKV2PALETTE ", true, false, true);
                }
                catch { }
            };
            menu.Items.Add(openPanel);

            // 退出/关闭菜单（可选）
            var close = new ToolStripMenuItem("关闭")
            {
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };
            close.Click += (s, e) => { try { menu.Close(); } catch { } };
            menu.Items.Add(close);

            return menu;
        }
    }

    // ---------------- 黑色主题菜单渲染 ----------------

    internal sealed class BlackMenuRenderer : ToolStripProfessionalRenderer
    {
        public BlackMenuRenderer() : base(new BlackColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(Point.Empty, e.Item.Size);
            var bg = e.Item.Selected ? Color.FromArgb(35, 35, 35) : Color.Black;
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, r);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            var rect = e.Item.ContentRectangle;
            var y = rect.Top + rect.Height / 2;
            using (var p = new Pen(Color.FromArgb(60, 60, 60)))
                g.DrawLine(p, rect.Left + 6, y, rect.Right - 6, y);
        }
    }

    internal sealed class BlackColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(80, 80, 80);
        public override Color ToolStripDropDownBackground => Color.Black;

        public override Color ImageMarginGradientBegin => Color.Black;
        public override Color ImageMarginGradientMiddle => Color.Black;
        public override Color ImageMarginGradientEnd => Color.Black;

        public override Color MenuItemSelected => Color.FromArgb(35, 35, 35);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(35, 35, 35);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(35, 35, 35);
    }
}
