using System;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace cadstockv2
{
    public class DropdownCommands
    {
        // 你工具栏按钮绑定的命令：ESCESC + "_CADSTOCKV2DROPDOWN "
        [CommandMethod("_CADSTOCKV2DROPDOWN", CommandFlags.Modal)]
        public void CADSTOCKV2DROPDOWN()
        {
            try
            {
                StockQuoteService.Instance.Start(); // 确保启动
                var doc = Application.DocumentManager.MdiActiveDocument;
                var ed = doc?.Editor;

                var list = StockQuoteService.Instance.GetSnapshot();

                // 没数据时给提示
                if (list == null || list.Count == 0)
                {
                    var err = StockQuoteService.Instance.LastError;
                    var t = StockQuoteService.Instance.LastUpdate;
                    ed?.WriteMessage("\n[cadstockv2] 暂无行情数据。LastUpdate=" + (t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm:ss") : "无")
                        + (string.IsNullOrWhiteSpace(err) ? "" : ("\n[cadstockv2] LastError=" + err)));
                    return;
                }

                // 这里如果你之前是弹 ContextMenuStrip，就继续走你 WinForms 那套（我不在这里重复造菜单）
                // 为了不破坏你现有 UI：这里先简单把前几条写到命令行，确认数据是活的
                var top = list.Take(8).ToList();
                ed?.WriteMessage("\n[cadstockv2] 行情(" + top.Count + "/" + list.Count + ") 更新:"
                    + (StockQuoteService.Instance.LastUpdate.HasValue ? StockQuoteService.Instance.LastUpdate.Value.ToString("HH:mm:ss") : "无"));

                foreach (var q in top)
                {
                    ed?.WriteMessage($"\n  {q.Symbol} {q.Name} {q.Price} ({q.ChangePercent:+0.00;-0.00;0.00}%)");
                }

                // TODO: 你原来如果是 ShowDropdownMenu()，就把这里改成调用你自己的菜单显示函数
                // ShowContextMenuAtCursor(list);

            }
            catch (System.Exception ex)
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor?.WriteMessage("\n[cadstockv2] DROPDOWN error: " + ex.GetType().Name + " - " + ex.Message);
            }
        }
    }
}
