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

        // ✅ 设置/增删股票（不需要 CADSTOCKV2PALETTE）
        // 用法：
        // 1) 输入 "600000,000001" => 覆盖式设置（基于当前列表增补/删除）
        // 2) 删除：输入 "-600000 -000001"
        // 3) 清空：输入 "-" 或 "clear"
        [CommandMethod("CADSTOCKV2SET")]
        public void CADSTOCKV2SET()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (ed == null) return;

            try
            {
                StockQuoteService.Instance.Start();

                // 当前列表（默认值）
                var current = StockQuoteService.Instance.GetSnapshot()
                    .Select(q => q.Symbol)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var defaultText = current.Count > 0
                    ? string.Join(",", current)
                    : "600000,000001,600519";

                var pso = new PromptStringOptions(
                    "\n输入股票代码：新增 600000；删除 -600000；清空输入 - 或 clear：")
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

                // 清空全部
                if (input == "-" || input.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    PaletteHost.ApplySymbols(Array.Empty<string>());
                    ed.WriteMessage("\n[cadstockv2] 已清空股票列表。");
                    return;
                }

                // token: 逗号/空格/换行/分号
                var tokens = input.Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' },
                                         StringSplitOptions.RemoveEmptyEntries);

                // 规范化（与 StockQuoteService 的 NormalizeSymbol 保持一致的最小实现）
                Func<string, string> norm = s =>
                {
                    s = (s ?? "").Trim();
                    if (s.Length >= 2)
                    {
                        var p = s.Substring(0, 2);
                        if (p.Equals("sh", StringComparison.OrdinalIgnoreCase) ||
                            p.Equals("sz", StringComparison.OrdinalIgnoreCase))
                            s = s.Substring(2);
                    }
                    if (s.Length > 2 && s[1] == '.' && (s[0] == '0' || s[0] == '1'))
                        s = s.Substring(2);
                    return s.Trim();
                };

                var set = new System.Collections.Generic.HashSet<string>(
                    current.Where(x => !string.IsNullOrWhiteSpace(x)).Select(norm),
                    StringComparer.OrdinalIgnoreCase
                );

                foreach (var t0 in tokens)
                {
                    var t = (t0 ?? "").Trim();
                    if (t.Length == 0) continue;

                    bool isRemove = t.StartsWith("-");
                    if (isRemove) t = t.Substring(1).Trim();
                    if (t.Length == 0) continue;

                    var sym = norm(t);
                    if (string.IsNullOrWhiteSpace(sym)) continue;

                    if (isRemove) set.Remove(sym);
                    else set.Add(sym);
                }

                var finalSyms = set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

                PaletteHost.ApplySymbols(finalSyms);
                StockQuoteService.Instance.ForceRefresh();

                ed.WriteMessage("\n[cadstockv2] 当前股票列表： " +
                                (finalSyms.Length == 0 ? "(空)" : string.Join(", ", finalSyms)));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[cadstockv2] CADSTOCKV2SET failed: " + ex.Message);
            }
        }
    }
}
