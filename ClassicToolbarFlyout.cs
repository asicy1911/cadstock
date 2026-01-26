using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace cadstockv2
{
    internal static class ClassicToolbarFlyout
    {
        // 主工具栏名称（显示在工具栏标题上）
        private const string MainToolbarName = "cadstock v2";
        // 下拉列表对应的“飞出工具栏”名称（内部用）
        private const string FlyoutToolbarName = "cadstock v2 presets";

        // ESC ESC 宏（ActiveX 文档示例用 Chr(3) + Chr(3)）:contentReference[oaicite:1]{index=1}
        private const string ESCESC = "\x03\x03";

        public static void TryInstall()
        {
            try
            {
                dynamic acadApp = Application.AcadApplication;
                if (acadApp == null) return;

                // 通常 Item(0) 是主菜单组（ACAD）
                dynamic menuGroup = acadApp.MenuGroups.Item(0);
                if (menuGroup == null) return;

                dynamic toolbars = menuGroup.Toolbars;
                if (toolbars == null) return;

                // 1) 找/建 主工具栏
                dynamic mainTb = FindToolbar(toolbars, MainToolbarName) ?? toolbars.Add(MainToolbarName);

                // 2) 找/建 飞出工具栏（下拉列表）
                dynamic flyTb = FindToolbar(toolbars, FlyoutToolbarName) ?? toolbars.Add(FlyoutToolbarName);

                // 3) 如果 flyTb 还没填充过，则填充预置按钮
                if ((int)flyTb.Count == 0)
                {
                    AddBtn(flyTb, "P0_All", "四大指数", ESCESC + "_CADSTOCKV2P0 ");
                    AddBtn(flyTb, "P1_SH",  "上证指数", ESCESC + "_CADSTOCKV2P1 ");
                    AddBtn(flyTb, "P2_SZ",  "深证成指", ESCESC + "_CADSTOCKV2P2 ");
                    AddBtn(flyTb, "P3_CYB", "创业板指", ESCESC + "_CADSTOCKV2P3 ");
                    AddBtn(flyTb, "P4_HS300","沪深300", ESCESC + "_CADSTOCKV2P4 ");
                    AddBtn(flyTb, "P5_SH50","上证50",  ESCESC + "_CADSTOCKV2P5 ");
                    AddBtn(flyTb, "P6_ZZ500","中证500", ESCESC + "_CADSTOCKV2P6 ");
                    AddBtn(flyTb, "P7_KC50","科创50",  ESCESC + "_CADSTOCKV2P7 ");
                }

                // 4) 如果 mainTb 还没装过按钮，则装：打开/刷新 + 一个“下拉按钮”
                if ((int)mainTb.Count == 0)
                {
                    AddBtn(mainTb, "Open", "打开面板", ESCESC + "_CADSTOCKV2 ");
                    mainTb.AddSeparator((int)mainTb.Count);

                    // ✅ flyout 按钮：带下拉三角
                    dynamic flyoutBtn = mainTb.AddToolbarButton(
                        (int)mainTb.Count,
                        "Indices",
                        "选择指数（下拉）",
                        ESCESC + "_CADSTOCKV2P0 ",
                        true // FlyoutButton = True  :contentReference[oaicite:2]{index=2}
                    );

                    // 绑定 flyout toolbar 到这个按钮  :contentReference[oaicite:3]{index=3}
                    flyoutBtn.AttachToolbarToFlyout(menuGroup.Name, flyTb.Name);

                    mainTb.AddSeparator((int)mainTb.Count);
                    AddBtn(mainTb, "Refresh", "刷新", ESCESC + "_CADSTOCKV2REFRESH ");
                    AddBtn(mainTb, "Stop", "关闭", ESCESC + "_CADSTOCKV2STOP ");
                }

                // 5) 显示主工具栏；飞出工具栏不单独显示
                mainTb.Visible = true;
                flyTb.Visible = false;
            }
            catch
            {
                // 安装失败不影响插件其它功能
            }
        }

        private static dynamic FindToolbar(dynamic toolbars, string name)
        {
            try
            {
                int count = (int)toolbars.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic tb = toolbars.Item(i);
                    string tbName = (string)(tb?.Name ?? "");
                    if (string.Equals(tbName, name, StringComparison.OrdinalIgnoreCase))
                        return tb;
                }
            }
            catch { }
            return null;
        }

        private static void AddBtn(dynamic toolbar, string name, string help, string macro)
        {
            // Index：末尾插入用 Count
            toolbar.AddToolbarButton((int)toolbar.Count, name, help, macro);
        }
    }
}
