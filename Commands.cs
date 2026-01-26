using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class Commands
    {
        [CommandMethod("CADSTOCKV2")]
        public void CADSTOCKV2()
        {
            StockQuoteService.Instance.Start();
            PaletteHost.ShowPalette();
        }

        [CommandMethod("CADSTOCKV2REFRESH")]
        public void CADSTOCKV2REFRESH()
        {
            StockQuoteService.Instance.ForceRefresh();
        }

        [CommandMethod("CADSTOCKV2STOP")]
        public void CADSTOCKV2STOP()
        {
            StockQuoteService.Instance.Stop();
            PaletteHost.HidePalette();
        }
    }
}
