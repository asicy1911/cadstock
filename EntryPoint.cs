using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            // 方案B：CUI 工具栏 + 下拉菜单
            ClassicToolbarInstall.InstallDeferred();
            DropdownCommands.Install();

            // ✅ 启动行情服务
            StockQuoteService.Instance.Start();

            // ✅ 面板自动刷新（关键：没有这句，面板就不会跟着更新）
            StockQuoteService.Instance.DataUpdated += PaletteHost.NotifyQuotesUpdated;

            // 进来先触发一次，让面板/菜单第一次打开就有东西
            StockQuoteService.Instance.ForceRefresh();
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
