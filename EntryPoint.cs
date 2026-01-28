using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstockv2.Commands))]
[assembly: CommandClass(typeof(cadstockv2.DropdownCommands))]
[assembly: CommandClass(typeof(cadstockv2.ToolbarCommands))]
[assembly: ExtensionApplication(typeof(cadstockv2.EntryPoint))]

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            // ✅ 方案 B：工具栏由 CUI/工作空间管理，所以不再自动创建“cadstock v2”经典工具栏
            // ClassicToolbarInstall.InstallDeferred();

            // ✅ 关键：订阅行情更新事件，让面板能自动刷新
            // 防止 NETLOAD/重复加载导致重复订阅：先 -= 再 +=
            StockQuoteService.Instance.DataUpdated -= PaletteHost.NotifyQuotesUpdated;
            StockQuoteService.Instance.DataUpdated += PaletteHost.NotifyQuotesUpdated;

            // 可选：启动行情服务（不强制，点菜单/打开面板时也会自动 Start）
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
