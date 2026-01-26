using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

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
            // 方案 B：不再创建 “cadstock v2” 经典工具栏
            // 只在 UI 就绪后把旧的 “cadstock v2” 工具栏自动隐藏
            ClassicToolbarInstall.HideLegacyToolbarDeferred(delete: true);

            // 可选：启动行情服务（建议保留，菜单/面板会更快有数据）
            StockQuoteService.Instance.Start();
        }

        public void Terminate()
        {
            StockQuoteService.Instance.Stop();
            PaletteHost.DisposePalette();
        }
    }
}
