namespace MaintPlan.IO.Results;

/// <summary>表のキーの列。Order は行を並べる順を決める値。</summary>
internal sealed record KeyColumn<TEntry>(string Name, Func<TEntry, string> Text, Func<TEntry, IComparable> Order);

/// <summary>表の値の列。1行にまとめた項目から、セルの文字を作る。</summary>
internal sealed record ValueColumn<TEntry>(string Name, Func<IReadOnlyList<TEntry>, string> Text);

/// <summary>
/// 項目を、渡された列名にあるキーの列の値ごとに1行にまとめた表。
/// 列名にないキーの列は、その値で分けずにまとめる。行はキーの列の順に並べる。
/// </summary>
internal static class GroupedTable
{
    public static TextTable Build<TEntry>(
        string tableName,
        IReadOnlyList<string> columns,
        IEnumerable<TEntry> entries,
        IReadOnlyList<KeyColumn<TEntry>> keys,
        IReadOnlyList<ValueColumn<TEntry>> values)
    {
        var unknown = columns.Where(column => keys.All(key => key.Name != column) && values.All(value => value.Name != column)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"列「{string.Join("」「", unknown)}」は「{tableName}」の列ではありません。", nameof(columns));
        }

        var usedKeys = keys.Where(key => columns.Contains(key.Name)).ToList();
        IOrderedEnumerable<List<TEntry>> groups = entries
            .GroupBy(entry => string.Join("\u001f", usedKeys.Select(key => key.Text(entry))))
            .Select(group => group.ToList())
            .OrderBy(_ => 0);
        foreach (var key in usedKeys)
        {
            groups = groups.ThenBy(group => key.Order(group[0]));
        }

        var rows = groups
            .Select(group => (IReadOnlyList<string>)[.. columns.Select(column => CellOf(column, group, keys, values))])
            .ToList();
        return new TextTable(columns, rows);
    }

    private static string CellOf<TEntry>(
        string column, List<TEntry> group, IReadOnlyList<KeyColumn<TEntry>> keys, IReadOnlyList<ValueColumn<TEntry>> values) =>
        keys.FirstOrDefault(key => key.Name == column) is { } keyColumn
            ? keyColumn.Text(group[0])
            : values.First(value => value.Name == column).Text(group);
}
