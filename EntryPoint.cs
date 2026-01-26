using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstockv2.Commands))]
[assembly: ExtensionApplication(typeof(cadstockv2.EntryPoint))]

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
{
    ClassicToolbarDropdownStocks.TryInstall();
}


        public void Terminate()
        {
            PaletteHost.DisposePalette();
        }
    }
}
