using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstockv2.Commands))]
[assembly: CommandClass(typeof(cadstockv2.ToolbarCommands))]
[assembly: CommandClass(typeof(cadstockv2.DropdownCommands))]
[assembly: ExtensionApplication(typeof(cadstockv2.EntryPoint))]

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            ClassicToolbarDropdownStocks.InstallDeferred();
        }

        public void Terminate()
        {
            PaletteHost.DisposePalette();
        }
    }
}
