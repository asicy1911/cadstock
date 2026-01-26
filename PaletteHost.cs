using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

namespace cadstock
{
    internal static class PaletteHost
    {
        private static PaletteSet _ps;
        private static QuotesPaletteControl _ctrl;

        public static void Toggle()
        {
            if (_ps == null)
            {
                _ctrl = new QuotesPaletteControl();
                _ps = new PaletteSet("cadstock")
                {
                    Style = PaletteSetStyles.ShowAutoHideButton |
                            PaletteSetStyles.ShowCloseButton |
                            PaletteSetStyles.ShowPropertiesMenu,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(380, 220),
                    Size = new System.Drawing.Size(560, 340),
                    Visible = true
                };

                _ps.Add("行情", _ctrl);
                _ctrl.Start();
            }
            else
            {
                _ps.Visible = !_ps.Visible;
            }
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
        [CommandMethod("IDX")]
        public void IDX() => PaletteHost.Toggle();

        [CommandMethod("IDXSTOP")]
        public void IDXSTOP() => PaletteHost.DisposePalette();
    }
}
