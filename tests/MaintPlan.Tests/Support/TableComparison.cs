using MaintPlan.IO.Results;

namespace MaintPlan.Tests.Support;

/// <summary>計算結果の表を、期待値と比べる。</summary>
public static class TableComparison
{
    /// <summary>食い違いを、1件ずつ文にして返す。食い違いがなければ空。</summary>
    public static IReadOnlyList<string> Compare(ExpectedTable expected, TextTable actual)
    {
        var differences = new List<string>();
        var missingColumns = expected.KeyColumns.Concat(expected.ValueColumns).Where(column => !actual.Columns.Contains(column)).ToList();
        if (missingColumns.Count > 0)
        {
            differences.Add($"計算結果の表に列「{string.Join("」「", missingColumns)}」がありません。");
            return differences;
        }

        var actualRows = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var row in actual.Rows)
        {
            var key = expected.KeyOf(actual.Columns, row);
            if (!actualRows.TryAdd(key, row))
            {
                differences.Add($"計算結果の表に、キーが同じ行が2つ以上あります: {Describe(expected, actual.Columns, row)}");
            }
        }

        var expectedKeys = new HashSet<string>();
        foreach (var row in expected.Table.Rows)
        {
            var key = expected.KeyOf(expected.Columns, row.Cells);
            expectedKeys.Add(key);
            actualRows.TryGetValue(key, out var actualRow);
            foreach (var column in expected.ValueColumns)
            {
                var type = expected.Kind.TypeOf(column);
                var expectedCell = row.Cells[ExpectedTable.IndexOf(expected.Columns, column)];
                if (expectedCell.Length == 0)
                {
                    continue;
                }

                if (actualRow is null)
                {
                    if (!type.IsZero(expectedCell))
                    {
                        differences.Add($"期待値 {row.LineNumber}行目({Describe(expected, expected.Columns, row.Cells)})の行が、計算結果にありません。");
                        break;
                    }

                    continue;
                }

                var actualCell = actualRow[ExpectedTable.IndexOf(actual.Columns, column)];
                if (type.Normalize(expectedCell) != type.Normalize(actualCell))
                {
                    differences.Add($"期待値 {row.LineNumber}行目({Describe(expected, expected.Columns, row.Cells)})の「{column}」: 期待値「{expectedCell}」、計算結果「{actualCell}」");
                }
            }
        }

        if (!expected.IsPartial)
        {
            foreach (var (key, row) in actualRows.Where(pair => !expectedKeys.Contains(pair.Key)))
            {
                var nonZero = expected.ValueColumns
                    .Where(column => !expected.Kind.TypeOf(column).IsZero(row[ExpectedTable.IndexOf(actual.Columns, column)]))
                    .ToList();
                if (nonZero.Count > 0)
                {
                    differences.Add($"期待値にない行({Describe(expected, actual.Columns, row)})の「{string.Join("」「", nonZero)}」が0ではありません。");
                }
            }
        }

        return differences;
    }

    private static string Describe(ExpectedTable expected, IReadOnlyList<string> columns, IReadOnlyList<string> cells) =>
        expected.KeyColumns.Count == 0
            ? "キーなし"
            : string.Join("、", expected.KeyColumns.Select(column => $"{column}={cells[ExpectedTable.IndexOf(columns, column)]}"));
}
