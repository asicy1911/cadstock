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
    public class DropdownCommands
    {
        private static ContextMenuStrip _menu;
        private static Control _owner;              // 用于 Show(Control, Point) 的 owner（不要 using）
        private static ContextMenuStrip _disposeMenuPending;
        private static TempOwnerForm _disposeOwnerPending;

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

                // 先关旧的（不要在这里 Dispose 正在显示的那个，避免重入）
                try { _menu?.Close(); } catch { }

                _menu = BuildMenu(ed);

                // ✅ 关闭时不要立刻 Dispose（会在点击消息链里炸）
                _menu.Closed += (s, e) =>
                {
                    try
                    {
                        _disposeMenuPending = _menu;
                        _disposeOwnerPending = _owner as TempOwnerForm;

                        _menu = null;
                        _owner = null;

                        AcApp.Idle -= OnIdleDispose;
                        AcApp.Idle += OnIdleDispose;
                    }
                    catch { }
                };

                // 准备 owner（要长期存活到菜单关闭）
                _owner = CreateOwnerHostControl();

                var screenPt = Control.MousePosition;

                // 轻微延后，避免命令焦点抢占导致不弹
                var t = new System.Windows.Forms.Timer { Interval = 1 };
                t.Tick += (ss, ee) =>
                {
                    t.Stop();
                    t.Dispose();

                    try
                    {
                        if (_menu == null) return;

                        if (_owner != null && !_owner.IsDisposed)
                        {
                            var clientPt = _owner.PointToClient(screenPt);
                            _menu.Show(_owner, clientPt);
                        }
                        else
                        {
                            _menu.Show(screenPt); // 兜底：屏幕坐标
                        }
                    }
                    catch (System.Exception ex)
                    {
                        try { ed.WriteMessage("\n[cadstockv2] Dropdown show failed: " + ex.Message); } catch { }
                        try { _menu?.Show(screenPt); } catch { }
                    }
                };
                t.Start();
            }
            catch (System.Exception ex)
            {
                try { ed?.WriteMessage("\n[cadstockv2] Dropdown failed: " + ex.GetType().Name + " - " + ex.Message); } catch { }
            }
        }

        private static void OnIdleDispose(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdleDispose;

            // ✅ 现在再 Dispose，避开 WinForms 的 OnItemClicked/Close 内部流程
            try { _disposeMenuPending?.Dispose(); } catch { }
            _disposeMenuPending = null;

            try
            {
                if (_disposeOwnerPending != null && !_disposeOwnerPending.IsDisposed)
                {
                    _disposeOwnerPending.Close();
                    _disposeOwnerPending.Dispose();
                }
            }
            catch { }
            _disposeOwnerPending = null;
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
            menu.Renderer = new BlackMenuRenderer();

            var last = StockQuoteService.Instance.LastUpdate;
            var err = StockQuoteService.Instance.LastError;

            menu.Items.Add(new ToolStripMenuItem("cadstock v2")
            {
                Enabled = false,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            });

            string status;
            if (last.HasValue) status = "更新时间: " + last.Value.ToString("HH:mm:ss");
            else if (!string.IsNullOrWhiteSpace(err)) status = "未更新: " + err;
            else status = "未更新";

            menu.Items.Add(new ToolStripMenuItem(status)
            {
                Enabled = false,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro
            });

            menu.Items.Add(new ToolStripSeparator());

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
    var cp = q.ChangePercent; // decimal
    var color = cp >= 0 ? Color.IndianRed : Color.MediumSeaGreen;

    // ✅ 只显示：名称 + 涨跌幅
    // 例：浦发银行  +1.23%
    var text = $"{q.Name}  {(cp >= 0 ? "+" : "")}{cp:0.00}%";

    var item = new ToolStripMenuItem(text)
    {
        BackColor = Color.Black,
        ForeColor = color
    };

    // 点击：把这只设为关注并刷新（保持原逻辑）
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
                try { AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("CADSTOCKV2 ", true, false, true); }
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

        private static Control CreateOwnerHostControl()
        {
            try
            {
                var hwnd = GetAcadMainHwnd();
                if (hwnd != IntPtr.Zero)
                {
                    var c = Control.FromHandle(hwnd);
                    if (c != null) return c;
                }

                // 拿不到 Control 的话，用一个透明临时 Form 当 owner（必须保存到菜单关闭再释放）
                var f = new TempOwnerForm();
                f.Show();
                return f;
            }
            catch
            {
                return new TempOwnerForm(); // 极端兜底
            }
        }

        private static IntPtr GetAcadMainHwnd()
        {
            try
            {
                var p = typeof(AcApp).GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Static);
                var v = p?.GetValue(null, null);
                if (v == null) return IntPtr.Zero;

                if (v is IntPtr ip) return ip;

                var hp = v.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance);
                var hv = hp?.GetValue(v, null);
                if (hv is IntPtr hip) return hip;
            }
            catch { }
            return IntPtr.Zero;
        }
    }

    internal sealed class TempOwnerForm : Form
    {
        public TempOwnerForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Opacity = 0;
            TopMost = false;
            // 放到屏幕外
            Location = new Point(-32000, -32000);
        }
    }

    // ---------- 黑色菜单渲染 ----------
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
}
