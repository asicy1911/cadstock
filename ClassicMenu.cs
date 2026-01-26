using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace cadstockv2
{
    internal static class ClassicMenu
    {
        // 顶部菜单标题（显示在菜单栏）
        private const string MenuCaption = "cadstock v2";

        public static void TryInstall()
        {
            try
            {
                dynamic acadApp = Application.AcadApplication;
                if (acadApp == null) return;

                dynamic menuBar = acadApp.MenuBar;
                if (menuBar == null) return;

                // 防重复：已存在就不再创建
                if (FindExisting(menuBar, MenuCaption) != null) return;

                // 新建顶层下拉菜单
                dynamic pop = menuBar.Add(MenuCaption);

                // 菜单项：打开/刷新/停止
                AddItem(pop, "打开面板", "^C^C_CADSTOCKV2 ");
                AddItem(pop, "刷新", "^C^C_CADSTOCKV2REFRESH ");
                AddItem(pop, "关闭面板", "^C^C_CADSTOCKV2STOP ");
                AddSeparator(pop);

                // 预置列表（下拉显示列表）
                AddItem(pop, "四大指数（上证/深成/创业板/沪深300）", "^C^C_CADSTOCKV2P0 ");
                AddItem(pop, "上证指数 000001", "^C^C_CADSTOCKV2P1 ");
                AddItem(pop, "深证成指 399001", "^C^C_CADSTOCKV2P2 ");
                AddItem(pop, "创业板指 399006", "^C^C_CADSTOCKV2P3 ");
                AddItem(pop, "沪深300 000300", "^C^C_CADSTOCKV2P4 ");
                AddItem(pop, "上证50 000016", "^C^C_CADSTOCKV2P5 ");
                AddItem(pop, "中证500 000905", "^C^C_CADSTOCKV2P6 ");
                AddItem(pop, "科创50 000688", "^C^C_CADSTOCKV2P7 ");
            }
            catch
            {
                // 菜单创建失败不影响插件主体功能
            }
        }

        private static dynamic FindExisting(dynamic menuBar, string caption)
        {
            try
            {
                int count = (int)menuBar.Count;
                for (int i = 1; i <= count; i++)
                {
                    dynamic m = menuBar.Item(i);
                    string c = (string)(m?.Caption ?? "");
                    if (string.Equals(c.Trim(), caption, StringComparison.OrdinalIgnoreCase))
                        return m;
                }
            }
            catch { }
            return null;
        }

        private static void AddItem(dynamic popupMenu, string caption, string macro)
        {
            // COM 菜单索引从 1 开始，插在末尾用 Count+1
            int idx = (int)popupMenu.Count + 1;
            popupMenu.AddMenuItem(idx, caption, macro);
        }

        private static void AddSeparator(dynamic popupMenu)
        {
            int idx = (int)popupMenu.Count + 1;
            popupMenu.AddSeparator(idx);
        }
    }
}
