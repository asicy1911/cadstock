using System;
using System.Drawing;
using System.IO;
using Autodesk.AutoCAD.Runtime;
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
                EnsureIcon();

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

                // 只保证存在一个按钮：点击弹出动态菜单
                if (!HasButton(tb, BtnName))
                {
                    // 尝试清空旧残留
                    try
                    {
                        int c = (int)tb.Count;
                        for (int i = c; i >= 1; i--)
                        {
                            try { tb.Item(i).Delete(); } catch { }
                        }
                    }
                    catch { }

                    // ✅ 单按钮：图标不画倒三角，但点击后弹出动态 ContextMenuStrip
                    tb.AddToolbarButton(
                        (int)tb.Count,
                        BtnName,
                        "个股行情（下拉）",
                        ESCESC + "_CADSTOCKV2DROPDOWN ",
                        _iconMain
                    );
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

        private static void EnsureIcon()
        {
            if (!string.IsNullOrWhiteSpace(_iconMain) && File.Exists(_iconMain))
                return;

            var dir = Path.Combine(Path.GetTempPath(), "cadstockv2_icons");
            Directory.CreateDirectory(dir);

            _iconMain = Path.Combine(dir, "main.bmp");
            if (!File.Exists(_iconMain))
                CreateBmp(_iconMain);
        }

        // ✅ 图标不画倒三角
        private static void CreateBmp(string path)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Black);

            using var pen = new Pen(Color.Gainsboro, 1);
            g.DrawRectangle(pen, 1, 1, 13, 13);

            using var brush = new SolidBrush(Color.Gainsboro);
            g.FillRectangle(brush, 4, 4, 8, 2);
            g.FillRectangle(brush, 4, 7, 8, 2);
            g.FillRectangle(brush, 4, 10, 8, 2);

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

    // ✅ 工具栏重建命令（排查/重置用）
    public class ToolbarCommands
    {
        [CommandMethod("CADSTOCKV2TBRESET")]
        public void CADSTOCKV2TBRESET()
        {
            ClassicToolbarInstall.TryInstall(reset: true);
        }
    }
}
