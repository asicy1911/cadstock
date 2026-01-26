using Autodesk.AutoCAD.Windows;

namespace cadstockv2
{
    internal static class PaletteHost
    {
        private static PaletteSet _ps;
        private static QuotesPaletteControl _control;

        public static void ShowPalette()
        {
            if (_ps == null)
            {
                _control = new QuotesPaletteControl();
                _ps = new PaletteSet("cadstock v2")
                {
                    Visible = true
                };
                _ps.Add("Quotes", _control);
                _ps.Size = new System.Drawing.Size(420, 260);
            }

            _ps.Visible = true;
            _ps.KeepFocus = false;
        }

        public static void HidePalette()
        {
            if (_ps != null) _ps.Visible = false;
        }

        public static void DisposePalette()
        {
            try { _ps?.Dispose(); } catch { }
            _ps = null;
            _control = null;
        }

        public static void ApplySymbols(string[] symbols)
        {
            StockQuoteService.Instance.SetSymbols(symbols);
            _control?.SetSymbols(symbols);
        }

        public static void NotifyQuotesUpdated()
        {
            _control?.SafeReloadFromService();
        }
    }
}
