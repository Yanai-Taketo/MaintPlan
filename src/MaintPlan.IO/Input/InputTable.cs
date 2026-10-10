namespace MaintPlan.IO.Input;

/// <summary>
/// 読み込む表。CSV のファイルか、ブックのシート1枚から読んだもの。
/// Place は文で示す場所(「工事.csv」か「シート「工事」」)、Rows は読む行(セルは Columns と同じ順)。
/// Error は、この表の行(1行目が列名)と列を示す形の誤りを作る。
/// </summary>
internal sealed record InputTable(
    string FilePath,
    string Place,
    IReadOnlyList<string> Columns,
    IReadOnlyList<InputRow> Rows,
    Func<int?, string?, string, InputFormatException> Error)
{
    /// <summary>列の位置。なければ -1。</summary>
    public int IndexOf(string column)
    {
        for (var index = 0; index < Columns.Count; index++)
        {
            if (Columns[index] == column)
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>
/// 表の1行。LineNumber は行番号(1行目が列名)。Problems は、列を示さない、その行の形の誤りの文で、行のセルの誤りより前に示す。
/// </summary>
internal sealed record InputRow(int LineNumber, IReadOnlyList<InputCell> Cells, IReadOnlyList<string> Problems);
