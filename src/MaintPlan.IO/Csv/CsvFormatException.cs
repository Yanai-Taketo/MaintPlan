namespace MaintPlan.IO.Csv;

/// <summary>CSV の形や値が決まりに合わないときの例外。ファイル・行・列を示す。</summary>
public sealed class CsvFormatException : Exception
{
    public CsvFormatException(string filePath, int? lineNumber, string? column, string message)
        : base(Describe(filePath, lineNumber, column, message))
    {
        FilePath = filePath;
        LineNumber = lineNumber;
        Column = column;
    }

    public string FilePath { get; }

    /// <summary>ファイルの行番号(1行目が列名)。ファイル全体の問題なら null。</summary>
    public int? LineNumber { get; }

    public string? Column { get; }

    private static string Describe(string filePath, int? lineNumber, string? column, string message)
    {
        var place = Path.GetFileName(filePath);
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
