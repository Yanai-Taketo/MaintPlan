namespace MaintPlan.IO.Results;

/// <summary>列名と文字列の値で表した表。書き出す表と期待値の比較に使う。</summary>
public sealed record TextTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
