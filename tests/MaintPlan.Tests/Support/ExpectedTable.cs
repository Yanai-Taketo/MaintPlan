using MaintPlan.IO.Csv;

namespace MaintPlan.Tests.Support;

/// <summary>
/// 期待値ファイル。ファイル名が「_一部」で終わるものは、書いた行だけを比べる。
/// それ以外は、表にない行の値をすべて0として比べる。空欄のセルは比べない。
/// </summary>
public sealed record ExpectedTable(ExpectedKind Kind, bool IsPartial, CsvTable Table)
{
    public const string PartialSuffix = "_一部";

    public IReadOnlyList<string> Columns => Table.Columns;

    public IReadOnlyList<string> KeyColumns =>
        [.. Kind.Keys.Select(key => key.Name).Where(name => Columns.Contains(name))];

    public IReadOnlyList<string> ValueColumns =>
        [.. Columns.Where(Kind.Values.ContainsKey)];

    /// <summary>計算結果の表に置く列。比べない列(保存の確認の「結果」など)を除く。</summary>
    public IReadOnlyList<string> ResultColumns =>
        [.. Columns.Where(column => !(Kind.IgnoredColumns?.Contains(column) ?? false))];

    /// <summary>期待値ファイルを読み、形を確かめる。決まりに合わない点はまとめて例外にする。</summary>
    public static ExpectedTable Load(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var isPartial = stem.EndsWith(PartialSuffix, StringComparison.Ordinal);
        var kindName = isPartial ? stem[..^PartialSuffix.Length] : stem;
        var kind = ExpectedKinds.Find(kindName)
            ?? throw new CsvFormatException(filePath, null, null, $"「{kindName}」は期待値の種類ではありません。種類: {string.Join("、", ExpectedKinds.All.Select(k => k.Name))}");

        var table = CsvTable.Read(filePath);
        var expected = new ExpectedTable(kind, isPartial, table);
        var problems = expected.Check().ToList();
        if (problems.Count > 0)
        {
            throw new CsvFormatException(filePath, null, null, Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        return expected;
    }

    /// <summary>表の行のキー(キーの列の値を、比べる形に直して並べたもの)。</summary>
    public string KeyOf(IReadOnlyList<string> columns, IReadOnlyList<string> cells) =>
        string.Join("\u001f", KeyColumns.Select(column =>
        {
            var index = IndexOf(columns, column);
            return index < 0 ? string.Empty : Kind.TypeOf(column).Normalize(cells[index]);
        }));

    public static int IndexOf(IReadOnlyList<string> columns, string column)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index] == column)
            {
                return index;
            }
        }

        return -1;
    }

    private IEnumerable<string> Check()
    {
        foreach (var column in Columns.Where(column => !Kind.IsKnownColumn(column)))
        {
            yield return $"1行目: 列「{column}」は「{Kind.Name}」の列ではありません。";
        }

        foreach (var key in Kind.Keys.Where(key => key.Required && !Columns.Contains(key.Name)))
        {
            yield return $"1行目: 列「{key.Name}」がありません。";
        }

        if (Kind.ExclusiveKeys is { } exclusive && exclusive.Count(Columns.Contains) != 1)
        {
            yield return $"1行目: 列「{string.Join("」「", exclusive)}」のどれか1つを置きます。";
        }

        if (ValueColumns.Count == 0)
        {
            yield return $"1行目: 値の列({string.Join("、", Kind.Values.Keys)})が1つもありません。";
        }

        if (Table.Rows.Count == 0)
        {
            yield return "行が1つもありません。";
        }

        var keys = new HashSet<string>();
        foreach (var row in Table.Rows)
        {
            for (var index = 0; index < Columns.Count; index++)
            {
                var column = Columns[index];
                var cell = row.Cells[index];
                var isKey = KeyColumns.Contains(column);
                if (cell.Length == 0)
                {
                    if (isKey)
                    {
                        yield return $"{row.LineNumber}行目: キーの列「{column}」は空欄にできません。";
                    }

                    continue;
                }

                var type = Kind.TypeOf(column);
                if (!type.IsValid(cell))
                {
                    yield return $"{row.LineNumber}行目: 列「{column}」の「{cell}」は{type.Description}ではありません。";
                }
            }

            if (!keys.Add(KeyOf(Columns, row.Cells)))
            {
                yield return $"{row.LineNumber}行目: キーが同じ行がほかにもあります。";
            }
        }
    }
}
