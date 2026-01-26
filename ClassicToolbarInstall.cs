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
        private static string _bmp16;
        private static string _bmp32;

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
                EnsureIcons(forceRegen: false);

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

                // 单按钮
                if (!HasButton(tb, BtnName))
                {
                    // 清理旧残留
                    try
                    {
                        int c = (int)tb.Count;
                        for (int i = c; i >= 1; i--)
                        {
                            try { tb.Item(i).Delete(); } catch { }
                        }
                    }
                    catch { }

                    // ✅ 只传 4 个参数：不要传第 5 个 FlyoutButton
                    dynamic btn = tb.AddToolbarButton(
                        (int)tb.Count,
                        BtnName,
                        "cadstock v2",
                        ESCESC + "_CADSTOCKV2DROPDOWN "
                    );

                    // ✅ 用 SetBitmaps 设置图标（16/32 都给）
                    try
                    {
                        btn.SetBitmaps(_bmp16, _bmp32);
                    }
                    catch
                    {
                        // 有些版本只认一个，也可以同一个路径兜底
                        try { btn.SetBitmaps(_bmp16, _bmp16); } catch { }
                    }
                }

                tb.Visible = true;
                Write($"Toolbar OK: {ToolbarName}");
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

        private static void EnsureIcons(bool forceRegen)
        {
            var dir = Path.Combine(Path.GetTempPath(), "cadstockv2_icons");
            Directory.CreateDirectory(dir);

            _bmp16 = Path.Combine(dir, "main16.bmp");
            _bmp32 = Path.Combine(dir, "main32.bmp");

            if (forceRegen)
            {
                try { if (File.Exists(_bmp16)) File.Delete(_bmp16); } catch { }
                try { if (File.Exists(_bmp32)) File.Delete(_bmp32); } catch { }
            }

            if (!File.Exists(_bmp16)) CreateBmp(_bmp16, 16, 16);
            if (!File.Exists(_bmp32)) CreateBmp(_bmp32, 32, 32);
        }

        // 图标：三条线“列表”，不画倒三角；强制 32bpp，避免 Indexed
        private static void CreateBmp(string path, int w, int h)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);

                int pad = (w <= 16) ? 1 : 2;
                int box = w - pad - 3;

                using (var pen = new Pen(Color.Gainsboro, 1))
                    g.DrawRectangle(pen, pad, pad, box, box);

                using (var brush = new SolidBrush(Color.Gainsboro))
                {
                    int x = pad + (w <= 16 ? 3 : 6);
                    int y1 = pad + (w <= 16 ? 3 : 7);
                    int barW = (w <= 16 ? 8 : 16);
                    int barH = (w <= 16 ? 2 : 4);
                    int gap = (w <= 16 ? 3 : 6);

                    g.FillRectangle(brush, x, y1, barW, barH);
                    g.FillRectangle(brush, x, y1 + gap, barW, barH);
                    g.FillRectangle(brush, x, y1 + gap * 2, barW, barH);
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
