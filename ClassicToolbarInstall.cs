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

        private static bool _installed;
        private static string _iconSmall; // 16x16
        private static string _iconLarge; // 32x32

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
                EnsureIcons();

                dynamic acadApp = AcApp.AcadApplication;
                if (acadApp == null) { Write("AcadApplication == null"); return; }

                // 经典界面：一般 MenuGroups.Item(0) 就是 ACAD 主组
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

                // 清空旧按钮（避免残留）
                ClearToolbarItems(tb);

                // 创建单按钮
                dynamic btn = tb.AddToolbarButton(
                    GetSafeIndexForAdd(tb),
                    BtnName,
                    "cadstock v2",
                    "^C^C_CADSTOCKV2DROPDOWN "
                );

                // 绑定位图（否则经常显示 ?）
                TrySetBitmaps(btn, _iconLarge, _iconSmall);

                // 显示工具栏
                tb.Visible = true;

                Write($"Toolbar OK: {ToolbarName}");
            }
            catch (Exception ex)
            {
                Write("Toolbar install failed: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private static int GetSafeIndexForAdd(dynamic tb)
        {
            try
            {
                // COM 下有的从 0 开始，有的从 1 开始；AddToolbarButton 用 Count 通常安全
                int c = (int)tb.Count;
                return c; // 追加到末尾
            }
            catch { return 0; }
        }

        private static void TrySetBitmaps(dynamic button, string largeBmp, string smallBmp)
        {
            try
            {
                // AutoCAD 经典工具栏按钮通常支持 SetBitmaps(large, small)
                button.SetBitmaps(largeBmp, smallBmp);
            }
            catch (Exception ex)
            {
                Write("SetBitmaps failed: " + ex.Message);
            }
        }

        private static void ClearToolbarItems(dynamic tb)
        {
            try
            {
                int c = (int)tb.Count;
                // 尝试从大到小删除（不同版本 Item 索引可能 0/1 基）
                for (int i = c; i >= 0; i--)
                {
                    try
                    {
                        dynamic it = tb.Item(i);
                        it.Delete();
                    }
                    catch { }
                }
            }
            catch
            {
                // 备用：尝试 1-based
                try
                {
                    int c = (int)tb.Count;
                    for (int i = c; i >= 1; i--)
                    {
                        try
                        {
                            dynamic it = tb.Item(i);
                            it.Delete();
                        }
                        catch { }
                    }
                }
                catch { }
            }
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
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
            }
            catch { }

            // 备用：尝试 1-based
            try
            {
                int count = (int)toolbars.Count;
                for (int i = 1; i <= count; i++)
                {
                    dynamic t = toolbars.Item(i);
                    string n = (string)(t?.Name ?? "");
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
            }
            catch { }

            return null;
        }

        private static void EnsureIcons()
        {
            if (!string.IsNullOrWhiteSpace(_iconSmall) && File.Exists(_iconSmall) &&
                !string.IsNullOrWhiteSpace(_iconLarge) && File.Exists(_iconLarge))
                return;

            var dir = Path.Combine(Path.GetTempPath(), "cadstockv2_icons");
            Directory.CreateDirectory(dir);

            _iconSmall = Path.Combine(dir, "main_16.bmp");
            _iconLarge = Path.Combine(dir, "main_32.bmp");

            if (!File.Exists(_iconSmall)) CreateBmp4bpp(_iconSmall, 16, 16);
            if (!File.Exists(_iconLarge)) CreateBmp4bpp(_iconLarge, 32, 32);
        }

        /// <summary>
        /// 生成更“AutoCAD 经典工具栏友好”的 4bpp(16色) BMP
        /// 图案：列表框 3 条线（不画倒三角）
        /// </summary>
        private static void CreateBmp4bpp(string path, int w, int h)
        {
            // 先用 32bpp 画，再量化到 4bpp Indexed
            using (var src = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(src))
            {
                g.Clear(Color.Black);

                using (var pen = new Pen(Color.Gainsboro, 1))
                    g.DrawRectangle(pen, 1, 1, w - 3, h - 3);

                using (var brush = new SolidBrush(Color.Gainsboro))
                {
                    int left = (int)(w * 0.25);
                    int top1 = (int)(h * 0.28);
                    int top2 = (int)(h * 0.46);
                    int top3 = (int)(h * 0.64);
                    int barW = (int)(w * 0.50);
                    int barH = Math.Max(2, (int)(h * 0.10));

                    g.FillRectangle(brush, left, top1, barW, barH);
                    g.FillRectangle(brush, left, top2, barW, barH);
                    g.FillRectangle(brush, left, top3, barW, barH);
                }

                using (var dst = ConvertTo4bppIndexed(src))
                {
                    dst.Save(path, ImageFormat.Bmp);
                }
            }
        }

        private static Bitmap ConvertTo4bppIndexed(Bitmap src)
        {
            // 创建 4bpp 位图并填充一个简单 16 色调色板（黑/灰为主）
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format4bppIndexed);

            var pal = dst.Palette;
            // 16 色：黑到白的灰阶（够用）
            for (int i = 0; i < 16; i++)
            {
                int v = (int)(i * (255.0 / 15.0));
                pal.Entries[i] = Color.FromArgb(255, v, v, v);
            }
            dst.Palette = pal;

            // 逐像素映射到最接近的灰阶
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    var c = src.GetPixel(x, y);
                    // 灰度
                    int gray = (c.R + c.G + c.B) / 3;
                    int idx = (int)Math.Round(gray / 255.0 * 15.0);
                    if (idx < 0) idx = 0;
                    if (idx > 15) idx = 15;
                    dst.SetPixel(x, y, pal.Entries[idx]);
                }
            }

            return dst;
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
