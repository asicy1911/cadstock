public List<StockQuote> GetSnapshot()
{
    lock (_lock)
    {
        var result = new List<StockQuote>();

        // 按 symbols 顺序输出
        for (int i = 0; i < _symbols.Count; i++)
        {
            var s = _symbols[i];
            StockQuote q;
            if (_latest.TryGetValue(s, out q) && q != null)
                result.Add(q);
        }

        return result;
    }
}

public void SetSymbols(IEnumerable<string> symbols)
{
    lock (_lock)
    {
        _symbols.Clear();
        _symbols.AddRange(symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        // ✅ 清理 _latest 里已被移除的股票，否则 UI 还会显示旧的
        var keep = new HashSet<string>(_symbols, StringComparer.OrdinalIgnoreCase);
        var keys = _latest.Keys.ToList();
        foreach (var k in keys)
        {
            if (!keep.Contains(k))
                _latest.Remove(k);
        }

        SaveSymbols();
    }

    ForceRefresh();
}
