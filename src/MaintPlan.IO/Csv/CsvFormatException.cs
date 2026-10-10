using MaintPlan.IO.Input;

namespace MaintPlan.IO.Csv;

/// <summary>CSV の形や値が決まりに合わないときの例外。ファイル・行・列を示す。場所はファイル名とする。</summary>
public sealed class CsvFormatException : InputFormatException
{
    public CsvFormatException(string filePath, int? lineNumber, string? column, string message)
        : base(Path.GetFileName(filePath), lineNumber, column, message)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
