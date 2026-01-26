using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace cadstockv2
{
    /// <summary>
    /// 点击工具栏按钮触发的下拉菜单（AutoCAD 命令）
    /// </summary>
    public class DropdownCommands
    {
        // 保持引用：否则 Show 后立刻被 GC/Dispose 也会导致“看起来没弹”
        private static ContextMenuStrip _menu;

        // 这个命令名要和 ClassicToolbarInstall 里写的一致： "_CADSTOCKV2DROPDOWN "
        [CommandMethod("CADSTOCKV2DROPDOWN")]
        public void ShowDropdown()
        {
            Editor ed = null;

            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                ed = doc?.Editor;
                if (ed == null) return;

                StockQuoteService.Instance.Start();

                // 关掉旧菜单
                try
                {
                    if (_menu != null)
                    {
                        _menu.Close();
                        _menu.Dispose();
                        _menu = null;
                    }
                }
                catch { }

                _menu = BuildMenu(ed);
                _menu.Closed += (s, e) =>
                {
                    try { _menu?.Dispose(); } catch { }
                    _menu = null;
                };

                // 延后一点点再弹（避免命令刚执行完被 AutoCAD 抢焦点导致菜单立刻关闭/不显示）
                var pt = Control.MousePosition;

                var t = new System.Windows.Forms.Timer { Interval = 1 };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    t.Dispose();

                    try
                    {
                        var hwnd = GetAcadMainHwnd();
                        if (hwnd != IntPtr.Zero)
                            _menu.Show(new Win32Window(hwnd), pt); // pt 为屏幕坐标
                        else
                            _menu.Show(pt);
                    }
                    catch
                    {
                        try { _menu.Show(pt); } catch { }
                    }
                };
                t.Start();
            }
            catch (Exception ex)
            {
                try
                {
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

            // ✅ 黑色主题渲染（只改 BackColor 有时会被系统主题覆盖）
            menu.Renderer = new BlackMenuRenderer();

            // 标题/状态
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
                menu.Items.Add(new ToolStripMenuItem("暂无数据（稍等或点“立即刷新”）")
                {
                    Enabled = false,
                    BackColor = Color.Black,
                    ForeColor = Color.Gainsboro
                });
            }
            else
            {
                foreach (var q in list.Take(25))
                {
                    var color = q.ChangePercent >= 0 ? Color.IndianRed : Color.MediumSeaGreen;

                    var text = $"{q.Name}  {q.Symbol}  {q.Price:0.00}";
                    var item = new ToolStripMenuItem(text)
                    {
                        BackColor = Color.Black,
                        ForeColor = color
                    };

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

            var close = new ToolStripMenuItem("关闭")
            {
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            };
            close.Click += (s, e) => { try { menu.Close(); } catch { } };
            menu.Items.Add(close);

            return menu;
        }

        private static IntPtr GetAcadMainHwnd()
        {
            // 尽量兼容不同版本：MainWindow 可能是 IntPtr，也可能是对象带 Handle
            try
            {
                var p = typeof(AcApp).GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Static);
                if (p == null) return IntPtr.Zero;

                var v = p.GetValue(null, null);
                if (v == null) return IntPtr.Zero;

                if (v is IntPtr ip) return ip;

                var hp = v.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance);
                if (hp != null)
                {
                    var hv = hp.GetValue(v, null);
                    if (hv is IntPtr hip) return hip;
                }
            }
            catch { }

            return IntPtr.Zero;
        }
    }

    // ------------- 黑色主题渲染 -------------

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
            var rect = e.Item.ContentRectangle;
            var y = rect.Top + rect.Height / 2;
            using (var p = new Pen(Color.FromArgb(60, 60, 60)))
                e.Graphics.DrawLine(p, rect.Left + 6, y, rect.Right - 6, y);
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

    internal sealed class Win32Window : IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public Win32Window(IntPtr h) { Handle = h; }
    }
}
