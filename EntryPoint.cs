using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            // 面板自动刷新依赖这个事件
            StockQuoteService.Instance.DataUpdated += PaletteHost.NotifyQuotesUpdated;

            // 插件加载即启动服务（也会读 txt）
            StockQuoteService.Instance.Start();
        }

        public void Terminate()
        {
            try
            {
                StockQuoteService.Instance.DataUpdated -= PaletteHost.NotifyQuotesUpdated;
            }
            catch { }

            StockQuoteService.Instance.Stop();
            PaletteHost.DisposePalette();
        }
    }
}
