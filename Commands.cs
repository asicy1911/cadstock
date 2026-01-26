using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace cadstockv2
{
    public class Commands
    {
        [CommandMethod("CADSTOCKV2")]
        public void CADSTOCKV2()
        {
            StockQuoteService.Instance.Start();
            PaletteHost.ShowPalette();
        }

        [CommandMethod("CADSTOCKV2REFRESH")]
        public void CADSTOCKV2REFRESH()
        {
            StockQuoteService.Instance.ForceRefresh();
        }

        [CommandMethod("CADSTOCKV2STOP")]
        public void CADSTOCKV2STOP()
        {
            StockQuoteService.Instance.Stop();
            PaletteHost.HidePalette();
        }

        // ✅ 新增：设置股票列表（逗号/空格/换行/分号都支持；支持 sh600000 / sz000001）
        [CommandMethod("CADSTOCKV2SET")]
        public void CADSTOCKV2SET()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (ed == null) return;

            try
            {
                StockQuoteService.Instance.Start();

                // 先把当前列表作为默认值
                var current = StockQuoteService.Instance.GetSnapshot()
                    .Select(q => q.Symbol)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var defaultText = (current != null && current.Length > 0)
                    ? string.Join(",", current)
                    : "600000,000001,600519";

                var pso = new PromptStringOptions("\n输入股票代码（逗号/空格/换行分隔，支持sh/sz前缀）：")
                {
                    AllowSpaces = true,
                    DefaultValue = defaultText,
                    UseDefaultValue = true
                };

                var res = ed.GetString(pso);
                if (res.Status != PromptStatus.OK) return;

                var input = (res.StringResult ?? "").Trim();
                if (string.IsNullOrWhiteSpace(input))
                {
                    ed.WriteMessage("\n[cadstockv2] 输入为空，未修改。");
                    return;
                }

                var syms = input
                    .Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => (s ?? "").Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();

                if (syms.Length == 0)
                {
                    ed.WriteMessage("\n[cadstockv2] 未解析到任何股票代码。");
                    return;
                }

                PaletteHost.ApplySymbols(syms);
                StockQuoteService.Instance.ForceRefresh();

                ed.WriteMessage("\n[cadstockv2] 已设置股票： " + string.Join(", ", syms));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[cadstockv2] CADSTOCKV2SET failed: " + ex.Message);
            }
        }
    }
}
