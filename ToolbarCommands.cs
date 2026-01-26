using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace cadstockv2
{
    // 这个类必须存在：EntryPoint.cs 里 assembly: CommandClass 正在引用它
    public class ToolbarCommands
    {
        [CommandMethod("CADSTOCKV2TBRESET")]
        public void CADSTOCKV2TBRESET()
        {
            // 强制重建：删除旧工具栏（如果存在）并重新创建
            ClassicToolbarInstall.TryInstall(reset: true);

            try
            {
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n[cadstockv2] 工具栏已重建（reset=true）");
            }
            catch { }
        }

        [CommandMethod("CADSTOCKV2TB")]
        public void CADSTOCKV2TB()
        {
            // 非重建：只确保存在并显示
            ClassicToolbarInstall.TryInstall(reset: false);

            try
            {
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n[cadstockv2] 工具栏已确保显示（reset=false）");
            }
            catch { }
        }
    }
}
