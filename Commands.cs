using System;
using System.Collections.Generic;
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
            // ✅ 重读 txt + 立刻刷新列表 + 强制拉取
            StockQuoteService.Instance.ReloadSymbolsFromDisk(forceRefresh: true);
        }

        [CommandMethod("CADSTOCKV2STOP")]
        public void CADSTOCKV2STOP()
        {
            StockQuoteService.Instance.Stop();
            PaletteHost.HidePalette();
        }

        // CADSTOCKV2SET
        // - 输入 "sh000001,600000,000001" => 覆盖设置（按输入顺序）
        // - 增删模式：带 - 号，例如： "-600519 600900"
        // - 清空：输入 "-" 或 "clear"
        [CommandMethod("CADSTOCKV2SET")]
        public void CADSTOCKV2SET()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (ed == null) return;

            try
            {
                StockQuoteService.Instance.Start();

                var current = StockQuoteService.Instance.GetSymbols();
                var defaultText = current.Count > 0
                    ? string.Join(",", current)
                    : "sh600000,sz000001,sh600519";

                var pso = new PromptStringOptions(
                    "\n输入股票代码：覆盖输入 600000,sh000001；增删模式用 -600000；清空输入 - 或 clear：")
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

                if (input == "-" || input.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    PaletteHost.ApplySymbols(Array.Empty<string>());
                    ed.WriteMessage("\n[cadstockv2] 已清空股票列表。");
                    return;
                }

                var tokens = input.Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' },
                                         StringSplitOptions.RemoveEmptyEntries);

                bool hasRemoveToken = tokens.Any(t => (t ?? "").TrimStart().StartsWith("-", StringComparison.Ordinal));

                List<string> finalList;

                if (!hasRemoveToken)
                {
                    // ✅ 覆盖式设置（顺序按输入）
                    finalList = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var t0 in tokens)
                    {
                        var sym = StockQuoteService.NormalizeSymbol(t0);
                        if (string.IsNullOrWhiteSpace(sym)) continue;
                        if (seen.Add(sym)) finalList.Add(sym);
                    }
                }
                else
                {
                    // ✅ 增删模式：在当前列表上操作，新增追加到末尾
                    finalList = new List<string>(current);
                    var set = new HashSet<string>(finalList, StringComparer.OrdinalIgnoreCase);

                    foreach (var t00 in tokens)
                    {
                        var t = (t00 ?? "").Trim();
                        if (t.Length == 0) continue;

                        bool isRemove = t.StartsWith("-", StringComparison.Ordinal);
                        if (isRemove) t = t.Substring(1).Trim();
                        if (t.Length == 0) continue;

                        var sym = StockQuoteService.NormalizeSymbol(t);
                        if (string.IsNullOrWhiteSpace(sym)) continue;

                        if (isRemove)
                        {
                            if (set.Remove(sym))
                                finalList.RemoveAll(x => x.Equals(sym, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            if (set.Add(sym))
                                finalList.Add(sym);
                        }
                    }
                }

                PaletteHost.ApplySymbols(finalList.ToArray());

                ed.WriteMessage("\n[cadstockv2] 当前股票列表： " +
                                (finalList.Count == 0 ? "(空)" : string.Join(", ", finalList)));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n[cadstockv2] CADSTOCKV2SET failed: " + ex.Message);
            }
        }
    }
}
