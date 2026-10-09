namespace MaintPlan.IO.Input;

/// <summary>
/// 入力の形や値が決まりに合わないときの例外。場所(CSV のファイルか、ブックのシートかファイル)・行・列を示す。
/// CSV から読んだときは、派生の CsvFormatException にする。
/// </summary>
public class InputFormatException : Exception
{
    /// <summary>place は、文で示す場所(「工事.csv」「シート「工事」」「入力.xlsx」など)。</summary>
    public InputFormatException(string place, int? lineNumber, string? column, string message)
        : base(Describe(place, lineNumber, column, message))
    {
        Place = place;
        LineNumber = lineNumber;
        Column = column;
    }

    public string Place { get; }

    /// <summary>行番号(1行目が列名)。表やファイル全体の問題なら null。</summary>
    public int? LineNumber { get; }

    public string? Column { get; }

    private static string Describe(string place, int? lineNumber, string? column, string message)
    {
        if (lineNumber is not null)
        {
            place += $" {lineNumber}行目";
        }

        if (column is not null)
        {
            place += $" 列「{column}」";
        }

        return $"{place}: {message}";
    }
}
