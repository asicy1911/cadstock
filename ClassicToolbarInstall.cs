using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace cadstockv2
{
    internal static class ClassicToolbarInstall
    {
        private const string ToolbarName = "cadstock v2";
        private const string BtnName = "cadstockv2_quotes";
        private const string ESCESC = "\x03\x03";

        private static bool _installed;
        private static string _iconMain;

        public static void InstallDeferred()
        {
            if (_installed) return;
            AcApp.Idle -= OnIdle;
            AcApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdle;
            TryInstall(reset: false);
            _installed = true;
        }

        public static void TryInstall(bool reset)
        {
            try
            {
                EnsureIcon(forceRegen: false);

                dynamic acadApp = AcApp.AcadApplication;
                if (acadApp == null) { Write("AcadApplication == null"); return; }

                dynamic menuGroup = acadApp.MenuGroups.Item(0);
                if (menuGroup == null) { Write("MenuGroups.Item(0) == null"); return; }

                dynamic toolbars = menuGroup.Toolbars;
                if (toolbars == null) { Write("menuGroup.Toolbars == null"); return; }

                dynamic tb = FindToolbar(toolbars, ToolbarName);

                if (reset && tb != null)
                {
                    try { tb.Delete(); } catch { }
                    tb = null;
                }

                if (tb == null)
                    tb = toolbars.Add(ToolbarName);

                // 单按钮：不带倒三角图标
                if (!HasButton(tb, BtnName))
                {
                    // 清理旧残留（避免之前版本遗留一堆按钮）
                    try
                    {
                        int c = (int)tb.Count;
                        for (int i = c; i >= 1; i--)
                        {
                            try { tb.Item(i).Delete(); } catch { }
                        }
                    }
                    catch { }

                    tb.AddToolbarButton(
                        (int)tb.Count,
                        BtnName,
                        "cadstock v2",
                        ESCESC + "_CADSTOCKV2DROPDOWN ",
                        _iconMain
                    );
                }

                tb.Visible = true;
                Write($"Toolbar OK: {ToolbarName}");
            }
            catch (InvalidOperationException ioe)
            {
                // 你遇到的就是这个：Indexed Bitmap 不支持 SetPixel
                Write("Toolbar install failed: " + ioe.Message + " -> 强制重建图标后重试");

                try
                {
                    EnsureIcon(forceRegen: true);
                    // 再试一次
                    TryInstall(reset: false);
                }
                catch (Exception ex2)
                {
                    Write("Retry failed: " + ex2.GetType().Name + " - " + ex2.Message);
                }
            }
            catch (Exception ex)
            {
                Write("Toolbar install failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static bool HasButton(dynamic tb, string name)
        {
            try
            {
                int c = (int)tb.Count;
                for (int i = 1; i <= c; i++)
                {
                    dynamic item = tb.Item(i);
                    string n = (string)(item?.Name ?? "");
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static dynamic FindToolbar(dynamic toolbars, string name)
        {
            try
            {
                int count = (int)toolbars.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic t = toolbars.Item(i);
                    string n = (string)(t?.Name ?? "");
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            catch { }
            return null;
        }

        private static void EnsureIcon(bool forceRegen)
        {
            var dir = Path.Combine(Path.GetTempPath(), "cadstockv2_icons");
            Directory.CreateDirectory(dir);

            _iconMain = Path.Combine(dir, "main.bmp");

            if (forceRegen && File.Exists(_iconMain))
            {
                try { File.Delete(_iconMain); } catch { }
            }

            if (!File.Exists(_iconMain))
                CreateBmp32(_iconMain);
        }

        // 图标：不画倒三角，只画“列表”3条线；强制 32bpp，避免 Indexed
        private static void CreateBmp32(string path)
        {
            using (var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);

                using (var pen = new Pen(Color.Gainsboro, 1))
                    g.DrawRectangle(pen, 1, 1, 13, 13);

                using (var brush = new SolidBrush(Color.Gainsboro))
                {
                    g.FillRectangle(brush, 4, 4, 8, 2);
                    g.FillRectangle(brush, 4, 7, 8, 2);
                    g.FillRectangle(brush, 4, 10, 8, 2);
                }

                bmp.Save(path, ImageFormat.Bmp);
            }
        }

        private static void Write(string msg)
        {
            try
            {
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n[cadstockv2] " + msg);
            }
            catch { }
        }
    }
}
