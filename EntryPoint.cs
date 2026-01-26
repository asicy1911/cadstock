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
            // ✅ UI 就绪后再创建工具栏（经典界面更稳）
            ClassicToolbarInstall.InstallDeferred();

            // 可选：启动行情服务（不强制，打开菜单/面板时也会自动 start）
            StockQuoteService.Instance.Start();
        }

        public void Terminate()
        {
            StockQuoteService.Instance.Stop();
            PaletteHost.DisposePalette();
        }
    }
}
