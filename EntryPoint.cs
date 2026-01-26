using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstockv2.Commands))]
[assembly: CommandClass(typeof(cadstockv2.DropdownCommands))]
[assembly: ExtensionApplication(typeof(cadstockv2.EntryPoint))]

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            // ✅ 延迟到 Idle 再装，经典界面更稳
            ClassicToolbarDropdownStocks.InstallDeferred();
        }

        public void Terminate()
        {
            PaletteHost.DisposePalette();
        }
    }
}
