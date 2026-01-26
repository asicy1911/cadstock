using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstockv2.Commands))]
[assembly: ExtensionApplication(typeof(cadstockv2.EntryPoint))]

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            // ✅ 经典界面：创建菜单栏下拉
            ClassicMenu.TryInstall();
        }

        public void Terminate()
        {
            PaletteHost.DisposePalette();
        }
    }
}
