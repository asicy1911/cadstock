using System;
using System.Drawing;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using System.Windows.Forms;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace cadstockv2
{
    internal static class ClassicToolbarDropdownStocks
    {
        private const string MainToolbarName = "cadstock v2";
        private const string ESCESC = "\x03\x03";

        private static bool _installed;
        private static string _iconMain;
        private static string _iconArrow;

        public static void InstallDeferred()
        {
            if (_installed) return;

            // ✅ UI 就绪后再创建（很关键）
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
                StockQuoteService.Instance.Start();
                EnsureIcons();

                dynamic acadApp = AcApp.AcadApplication;
                if (acadApp == null) { Write("AcadApplication == null"); return; }

                dynamic menuGroup = acadApp.MenuGroups.Item(0);
                if (menuGroup == null) { Write("MenuGroups.Item(0) == null"); return; }

                dynamic toolbars = menuGroup.Toolbars;
                if (toolbars == null) { Write("menuGroup.Toolbars == null"); return; }

                dynamic tb = FindToolbar(toolbars, MainToolbarName);

                if (reset && tb != null)
                {
                    try { tb.Delete(); } catch { }
                    tb = null;
                }

                if (tb == null)
                {
                    tb = toolbars.Add(MainToolbarName);
                }

                // 关键：不要只靠 Count==0（有时会残留空按钮/分隔符）
                // 我们检查是否已经有 “Dropdown” 这个按钮名，没有就补齐
                if (!HasButton(tb, "Dropdown"))
                {
                    // 清空再建（防空壳/残留）
                    try
                    {
                        // 尝试删除所有 items（有些版本不支持逐个删，就跳过）
                        int c = (int)tb.Count;
                        for (int i = c; i >= 1; i--)
                        {
                            try { tb.Item(i).Delete(); } catch { }
                        }
                    }
                    catch { }

                    AddBtn(tb, "Open", "打开面板", ESCESC + "_CADSTOCKV2 ", _iconMain);
                    tb.AddSeparator((int)tb.Count);

                    // 倒三角按钮：弹出下拉（显示实时个股）
                    AddBtn(tb, "Dropdown", "下拉（个股行情）", ESCESC + "_CADSTOCKV2DROPDOWN ", _iconArrow);

                    tb.AddSeparator((int)tb.Count);
                    AddBtn(tb, "Refresh", "刷新面板", ESCESC + "_CADSTOCKV2REFRESH ", _iconMain);
                    AddBtn(tb, "Stop", "关闭面板", ESCESC + "_CADSTOCKV2STOP ", _iconMain);
                }

                tb.Visible = true;
                Write($"Toolbar OK: {MainToolbarName} (Visible={tb.Visible})");
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

        private static void AddBtn(dynamic toolbar, string name, string help, string macro, string bmpPath)
        {
            toolbar.AddToolbarButton((int)toolbar.Count, name, help, macro, bmpPath);
        }

        private static void EnsureIcons()
        {
            if (!string.IsNullOrWhiteSpace(_iconMain) && File.Exists(_iconMain) &&
                !string.IsNullOrWhiteSpace(_iconArrow) && File.Exists(_iconArrow))
                return;

            var dir = Path.Combine(Path.GetTempPath(), "cadstockv2_icons");
            Directory.CreateDirectory(dir);

            _iconMain = Path.Combine(dir, "main.bmp");
            _iconArrow = Path.Combine(dir, "arrow.bmp");

            if (!File.Exists(_iconMain)) CreateBmp(_iconMain, drawArrow: false);
            if (!File.Exists(_iconArrow)) CreateBmp(_iconArrow, drawArrow: true);
        }

        private static void CreateBmp(string path, bool drawArrow)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Black);

            using var pen = new Pen(Color.Gainsboro, 1);
            g.DrawRectangle(pen, 1, 1, 13, 13);

            if (drawArrow)
            {
                using var brush = new SolidBrush(Color.Gainsboro);
                Point[] tri = { new Point(5, 6), new Point(11, 6), new Point(8, 10) };
                g.FillPolygon(brush, tri);
            }
            else
            {
                using var brush = new SolidBrush(Color.Gainsboro);
                g.FillRectangle(brush, 5, 4, 6, 2);
                g.FillRectangle(brush, 5, 7, 6, 2);
                g.FillRectangle(brush, 5, 10, 6, 2);
            }

            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
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

    public class ToolbarCommands
{
    [CommandMethod("CADSTOCKV2TBRESET")]
    public void CADSTOCKV2TBRESET()
    {
        ClassicToolbarDropdownStocks.TryInstall(reset: true);
    }
}

}
