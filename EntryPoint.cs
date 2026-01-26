using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstock.Commands))]
[assembly: ExtensionApplication(typeof(cadstock.EntryPoint))]

namespace cadstock
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize() { }
        public void Terminate()
        {
            PaletteHost.DisposePalette();
        }
    }
}
