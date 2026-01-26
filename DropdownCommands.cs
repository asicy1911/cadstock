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
        // 保持引用：避免 Show 后对象被提前回收/释放
        private static ContextMenuStrip _menu;

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

                // 延后一点点再弹（避免命令/焦点抢占）
                var screenPt = Control.MousePosition;

                var t = new System.Windows.Forms.Timer { Interval = 1 };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    t.Dispose();

                    try
                    {
                        // ✅ 用一个临时宿主控件作为 owner（Show(Control, Point)）
                        using (var host = CreateOwnerHostControl())
                        {
                            if (host != null && !host.IsDisposed)
                            {
                                // Show(Control, Point) 的 Point 是相对于该 Control 的坐标
                                var clientPt = host.PointToClient(screenPt);
                                _menu.Show(host, clientPt);
                            }
                            else
                            {
                                _menu.Show(screenPt); // 兜底：屏幕坐标
                            }
                        }
                    }
                    catch
                    {
                        try { _menu.Show(screenPt); } catch { }
                    }
                };
                t.Start();
            }
            catch (System.Exception ex)
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

            // ✅ 黑色主题渲染（防止系统主题覆盖）
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
            if (last.HasValue)
                status = "更新时间: " + last.Value.ToString("HH:mm:ss");
            else if (!string.IsNullOrWhiteSpace(err))
                status = "未更新: " + err;
            else
                status = "未更新";

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
                    var color = q.ChangePercent >= 0 ? Color.IndianRed : Color.MediumSeaGreen;
                    var text = $"{q.Name}  {q.Symbol}  {q.Price:0.00}";

                    var sym = q.Symbol;
                    var item = new ToolStripMenuItem(text)
                    {
                        BackColor = Color.Black,
                        ForeColor = color
                    };
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

        /// <summary>
        /// 创建一个临时宿主控件，挂到 AutoCAD 主窗口上，用作 ContextMenuStrip.Show(Control, Point) 的 owner。
        /// </summary>
        private static Control CreateOwnerHostControl()
        {
            try
            {
                var hwnd = GetAcadMainHwnd();
                if (hwnd == IntPtr.Zero) return null;

                // Control.FromHandle：拿到主窗口的 WinForms 包装（可能为 null，取决于宿主）
                var owner = Control.FromHandle(hwnd);

                if (owner != null) return owner;

                // 如果 FromHandle 拿不到，就创建一个极小的透明控件并设置 Parent = 从句柄创建的 NativeWindow 包装不了，
                // 这里退化：用一个隐藏 Form 作为 owner（最稳）
                var f = new Form
                {
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Size = new Size(1, 1),
                    Opacity = 0,
                    TopMost = false
                };

                // 尝试把它设为 AutoCAD 主窗的 owned form（Win32 级别）
                // 注意：不强依赖，失败就当普通隐藏窗
                try
                {
                    var mi = typeof(Form).GetMethod("SetDesktopLocation", BindingFlags.Instance | BindingFlags.Public);
                    mi?.Invoke(f, new object[] { -32000, -32000 });
                }
                catch { }

                f.Show();
                return f;
            }
            catch
            {
                return null;
            }
        }

        private static IntPtr GetAcadMainHwnd()
        {
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

    // ---------------- 黑色菜单渲染 ----------------

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
