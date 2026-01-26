using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

namespace cadstockv2
{
    internal static class PaletteHost
    {
        private static PaletteSet _ps;
        private static QuotesPaletteControl _ctrl;

        private static void EnsureCreated(bool makeVisible)
        {
            if (_ps != null)
            {
                if (makeVisible) _ps.Visible = true;
                return;
            }

            _ctrl = new QuotesPaletteControl();

            _ps = new PaletteSet("cadstock v2")
            {
                Style = PaletteSetStyles.ShowAutoHideButton |
                        PaletteSetStyles.ShowCloseButton |
                        PaletteSetStyles.ShowPropertiesMenu,
                DockEnabled = DockSides.Left | DockSides.Right,
                MinimumSize = new System.Drawing.Size(380, 220),
                Size = new System.Drawing.Size(560, 300),
                Visible = makeVisible
            };

            _ps.Add("行情", _ctrl);
            _ctrl.Start();
        }

        public static void Toggle()
        {
            EnsureCreated(makeVisible: true);
            _ps.Visible = true;
        }

        public static void RefreshNow()
        {
            EnsureCreated(makeVisible: true);
            _ctrl?.RefreshNow();
        }

        public static void ApplySymbols(string[] symbols)
        {
            EnsureCreated(makeVisible: true);
            _ctrl?.SetSymbols(symbols);
            _ctrl?.RefreshNow();
        }

        public static void DisposePalette()
        {
            try { _ctrl?.Stop(); } catch { }
            _ctrl = null;

            try { _ps?.Dispose(); } catch { }
            _ps = null;
        }
    }

    public class Commands
    {
        // ✅ v2 命令名，不和 v1 冲突
        [CommandMethod("CADSTOCKV2")]
        public void CADSTOCKV2() => PaletteHost.Toggle();

        [CommandMethod("CADSTOCKV2REFRESH")]
        public void CADSTOCKV2REFRESH() => PaletteHost.RefreshNow();

        [CommandMethod("CADSTOCKV2STOP")]
        public void CADSTOCKV2STOP() => PaletteHost.DisposePalette();

        // ✅ 下拉菜单对应的预置命令
        [CommandMethod("CADSTOCKV2P0")]
        public void P0() => PaletteHost.ApplySymbols(new[] { "s_sh000001", "s_sz399001", "s_sz399006", "s_sh000300" });

        [CommandMethod("CADSTOCKV2P1")]
        public void P1() => PaletteHost.ApplySymbols(new[] { "s_sh000001" });

        [CommandMethod("CADSTOCKV2P2")]
        public void P2() => PaletteHost.ApplySymbols(new[] { "s_sz399001" });

        [CommandMethod("CADSTOCKV2P3")]
        public void P3() => PaletteHost.ApplySymbols(new[] { "s_sz399006" });

        [CommandMethod("CADSTOCKV2P4")]
        public void P4() => PaletteHost.ApplySymbols(new[] { "s_sh000300" });

        [CommandMethod("CADSTOCKV2P5")]
        public void P5() => PaletteHost.ApplySymbols(new[] { "s_sh000016" });

        [CommandMethod("CADSTOCKV2P6")]
        public void P6() => PaletteHost.ApplySymbols(new[] { "s_sh000905" });

        [CommandMethod("CADSTOCKV2P7")]
        public void P7() => PaletteHost.ApplySymbols(new[] { "s_sh000688" });
    }
}
