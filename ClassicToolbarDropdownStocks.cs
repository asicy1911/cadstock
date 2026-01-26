using System;
using System.Drawing;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using System.Windows.Forms;

namespace cadstockv2
{
    internal static class ClassicToolbarDropdownStocks
    {
        private const string MainToolbarName = "cadstock v2";
        private const string ESCESC = "\x03\x03";

        private static string _iconMain;
        private static string _iconArrow;

        public static void TryInstall()
        {
            try
            {
                // 启动行情缓存（给下拉菜单用）
                StockQuoteService.Instance.Start();

                EnsureIcons();

                dynamic acadApp = Application.AcadApplication;
                dynamic menuGroup = acadApp.MenuGroups.Item(0);
                dynamic toolbars = menuGroup.Toolbars;

                dynamic tb = FindToolbar(toolbars, MainToolbarName) ?? toolbars.Add(MainToolbarName);

                // 防重复：如果已有按钮就不再加
                if ((int)tb.Count == 0)
                {
                    // 主按钮：打开面板
                    tb.AddToolbarButton((int)tb.Count, "Open", "打开面板", ESCESC + "_CADSTOCKV2 ", _iconMain);

                    tb.AddSeparator((int)tb.Count);

                    // 倒三角按钮：弹出下拉列表
                    tb.AddToolbarButton((int)tb.Count, "Dropdown", "下拉（个股行情）", ESCESC + "_CADSTOCKV2DROPDOWN ", _iconArrow);

                    tb.AddSeparator((int)tb.Count);

                    tb.AddToolbarButton((int)tb.Count, "Refresh", "刷新面板", ESCESC + "_CADSTOCKV2REFRESH ", _iconMain);
                    tb.AddToolbarButton((int)tb.Count, "Stop", "关闭面板", ESCESC + "_CADSTOCKV2STOP ", _iconMain);
                }

                tb.Visible = true;
            }
            catch
            {
                // 不影响主体
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
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            catch { }
            return null;
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
                // 画一个倒三角
                using var brush = new SolidBrush(Color.Gainsboro);
                Point[] tri = { new Point(5, 6), new Point(11, 6), new Point(8, 10) };
                g.FillPolygon(brush, tri);
            }
            else
            {
                // 画一个小 “S” 形标记
                using var brush = new SolidBrush(Color.Gainsboro);
                g.FillRectangle(brush, 5, 4, 6, 2);
                g.FillRectangle(brush, 5, 7, 6, 2);
                g.FillRectangle(brush, 5, 10, 6, 2);
            }

            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
        }
    }

    public class DropdownCommands
    {
        [CommandMethod("CADSTOCKV2DROPDOWN")]
        public void CADSTOCKV2DROPDOWN()
        {
            // 弹出菜单（WinForms）
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false
            };

            var quotes = StockQuoteService.Instance.GetSnapshot(out var last);
            var header = new ToolStripMenuItem($"更新时间：{(last == DateTime.MinValue ? "无" : last.ToString("HH:mm:ss"))}")
            {
                Enabled = false
            };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            if (quotes.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("暂无数据（稍等或检查网络）") { Enabled = false });
            }
            else
            {
                foreach (var q in quotes)
                {
                    var item = new ToolStripMenuItem(q.ToMenuText())
                    {
                        ForeColor = q.GetColor()
                    };

                    item.Click += (s, e) =>
                    {
                        // 选择某只股票：把面板切到这只（或你也可以改成多只）
                        PaletteHost.ApplySymbols(new[] { q.Symbol });
                    };

                    menu.Items.Add(item);
                }
            }

            // 在鼠标位置弹出
            var p = Cursor.Position;
            menu.Show(p);
        }
    }
}
