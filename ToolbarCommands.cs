using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class ToolbarCommands
    {
        // 只安装（不删除旧工具栏）
        [CommandMethod("CADSTOCKV2TBINSTALL")]
        public void CADSTOCKV2TBINSTALL()
        {
            ClassicToolbarInstall.TryInstall(reset: false);
        }

        // 删除并重建（推荐你每次替换 dll 后跑一次）
        [CommandMethod("CADSTOCKV2TBRESET")]
        public void CADSTOCKV2TBRESET()
        {
            ClassicToolbarInstall.TryInstall(reset: true);
        }
    }
}
