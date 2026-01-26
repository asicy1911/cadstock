using System;
using System.IO;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace cadstockv2
{
    internal static class ClassicToolbarInstall
    {
        // ========= 方案 B：交给 CUI/工作空间，不再由 DLL 创建经典工具栏 =========
        // 改成 true 才会再次启用“自动创建 cadstock v2 工具栏”
        private const bool EnableClassicToolbar = false;

        private const string ToolbarName = "cadstock v2";
        private const string BtnName = "cadstockv2_quotes";
        private const string ESCESC = "\x03\x03";

        private static bool _installed;

        public static void InstallDeferred()
        {
            // 方案 B：禁用创建，只负责隐藏旧工具栏
            if (!EnableClassicToolbar)
            {
                HideLegacyToolbarDeferred(delete: false);
                return;
            }

            if (_installed) return;
            AcApp.Idle -= OnIdle;
            AcApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdle;

            if (!EnableClassicToolbar)
            {
                HideLegacyToolbar(delete: false);
                _installed = true;
                return;
            }

            TryInstall(reset: false);
            _installed = true;
        }

        /// <summary>
        /// 方案 B：启动后把旧的 “cadstock v2” 工具栏隐藏（或删除）
        /// </summary>
        public static void HideLegacyToolbarDeferred(bool delete)
        {
            AcApp.Idle -= OnHideIdle;
            _pendingDelete = delete;
            AcApp.Idle += OnHideIdle;
        }

        private static bool _pendingDelete = false;

        private static void OnHideIdle(object sender, EventArgs e)
        {
            AcApp.Idle -= OnHideIdle;
            HideLegacyToolbar(_pendingDelete);
        }

        /// <summary>
        /// 隐藏（或删除）旧的经典工具栏 “cadstock v2”
        /// </summary>
        public static void HideLegacyToolbar(bool delete)
        {
            try
            {
                dynamic acadApp = AcApp.AcadApplication;
                if (acadApp == null) return;

                dynamic menuGroup = acadApp.MenuGroups.Item(0);
                if (menuGroup == null) return;

                dynamic toolbars = menuGroup.Toolbars;
                if (toolbars == null) return;

                dynamic tb = FindToolbar(toolbars, ToolbarName);
                if (tb == null) return;

                try
                {
                    if (delete)
                    {
                        // 删除（更彻底；下次不会再出现在工具栏列表）
                        tb.Delete();
                    }
                    else
                    {
                        // 仅隐藏（安全；你随时还能手动显示）
                        tb.Visible = false;
                    }
                }
                catch { }
            }
            catch { }
        }

        // ====== 下面是旧的“自动创建工具栏”的逻辑；方案 B 默认不会走到 ======

        public static void TryInstall(bool reset)
        {
            // 方案 B：禁用创建，只负责隐藏旧工具栏
            if (!EnableClassicToolbar)
            {
                HideLegacyToolbar(delete: false);
                return;
            }

            try
            {
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

                    // 方案 B 下这里一般不会用到；保持空实现避免引用图标逻辑
                    try { btn.SetBitmaps("", ""); } catch { }
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
