using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(cadstockv2.Commands))]
[assembly: ExtensionApplication(typeof(cadstockv2.EntryPoint))]

namespace cadstockv2
{
    public class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            // ✅ 经典工具栏：创建“下拉（flyout）”控件
            ClassicToolbarFlyout.TryInstall();
        }

        public void Terminate()
        {
            PaletteHost.DisposePalette();
        }
    }
}
