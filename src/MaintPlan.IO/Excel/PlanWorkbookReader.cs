using System.Globalization;
using ClosedXML.Excel;
using MaintPlan.Core.Model;
using MaintPlan.IO.Input;

namespace MaintPlan.IO.Excel;

/// <summary>
/// 計算に使う9つのテーブルを、テーブルごとのシート(シート名はテーブル名)を置いたブック(.xlsx)から読む。ほかのシートは読まない。
/// シートの1行目を列名とし、値のあるセルが1つもない行は読まない。行は Excel の行番号で示す。
/// 値は CSV と同じ決まりで読み、文字でないセルは、列が受け付けるものだけを読む(RowReader)。
/// </summary>
public static class PlanWorkbookReader
{
    /// <summary>表示される文字を作る文化。マシンの文化によらず、日本語の Excel と同じ書き方にする。</summary>
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>
    /// Excel が、日付と時刻の書式で表示できない値(9999-12-31 より後の日付など)のセルに表示する文字。Excel は、セルを「#」で埋めて表示する。
    /// </summary>
    private const string Unshowable = "########";

    /// <summary>
    /// ブックを読む。形の違う値は、最初の1件で止めずにすべて集める。形の違う値がなければ、読んだ内容と、行のシートでの場所を返す。
    /// ブックとして開けないファイルは、ファイル名を場所とする形の誤り1件にする。
    /// ブックは、Excel で開いたままでも読めるように、ほかの書き込みと削除を許す共有で開く。
    /// ファイルが見つからないときと読めないときは、IOException か UnauthorizedAccessException をそのまま投げる。
    /// </summary>
    public static PlanReadResult Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var workbook = Open(stream);
        if (workbook is null)
        {
            return new PlanReadResult(null, null, [new InputFormatException(Path.GetFileName(path), null, null, "ブックとして読めません。")]);
        }

        return PlanTableReader.Read(table => ReadSheet(path, workbook, table));
    }

    /// <summary>ブックを読む。形の違う値があれば、最初の1件を投げる。</summary>
    public static PlanData ReadWorkbook(string path)
    {
        var result = Read(path);
        return result.Plan ?? throw result.Errors[0];
    }

    /// <summary>
    /// ブックを開く。ブックとして開けなければ null。ClosedXML は、壊れたファイルやパスワード付きのブックに、
    /// 中身によって違う種類の例外(FileFormatException、NullReferenceException など)を投げるので、種類によらず開けないものとする。
    /// </summary>
    private static XLWorkbook? Open(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception exception) when (exception is not (IOException or UnauthorizedAccessException))
        {
            return null;
        }
    }

    /// <summary>
    /// テーブルのシートを表にする。シートがないとき、1行目に数式のセルがあるとき、列名の行がないときは、InputFormatException を投げる。
    /// 列名は、1行目の値のあるセルの表示される文字を、左から順に取る。列名のない列に値がある行は、その行の形の誤りにする。
    /// </summary>
    private static InputTable ReadSheet(string path, XLWorkbook workbook, PlanTable table)
    {
        var place = $"シート「{Labels.Of(table)}」";
        InputFormatException Error(int? lineNumber, string? column, string message) => new(place, lineNumber, column, message);

        if (!workbook.Worksheets.TryGetWorksheet(Labels.Of(table), out var sheet))
        {
            throw Error(null, null, "シートがありません。");
        }

        var header = sheet.Row(1).CellsUsed(XLCellsUsedOptions.Contents).Where(HasValue).OrderBy(cell => cell.Address.ColumnNumber).ToList();
        if (header.Any(cell => cell.HasFormula))
        {
            throw Error(1, null, RowReader.FormulaMessage);
        }

        if (header.Count == 0)
        {
            throw Error(null, null, "列名の行がありません。");
        }

        var indexes = header.Select((cell, index) => (cell.Address.ColumnNumber, index)).ToDictionary();
        var rows = sheet.CellsUsed(XLCellsUsedOptions.Contents)
            .Where(cell => cell.Address.RowNumber > 1 && HasValue(cell))
            .GroupBy(cell => cell.Address.RowNumber)
            .OrderBy(cells => cells.Key)
            .Select(cells => ReadRow(cells.Key, cells, indexes))
            .ToList();
        return new InputTable(path, place, [.. header.Select(Shown)], rows, Error);
    }

    /// <summary>行の値のあるセルを、列名の順のセルにする(値のない列は空欄)。列名のない列のセルは読まず、その行の形の誤りにする。</summary>
    private static InputRow ReadRow(int rowNumber, IEnumerable<IXLCell> cells, IReadOnlyDictionary<int, int> indexes)
    {
        var row = Enumerable.Repeat(InputCell.Blank, indexes.Count).ToArray();
        var hasValueWithoutColumn = false;
        foreach (var cell in cells)
        {
            if (indexes.TryGetValue(cell.Address.ColumnNumber, out var index))
            {
                row[index] = CellOf(cell);
            }
            else
            {
                hasValueWithoutColumn = true;
            }
        }

        return new InputRow(rowNumber, row, hasValueWithoutColumn ? ["列名のない列に値があります。"] : []);
    }

    /// <summary>
    /// ブックのセルを、型と表示される文字を持つセルにする。文字のセルは、その文字で読む。
    /// 数式のセルは、計算した値を読まない(表示される文字も作らない)。日付にできない値の日付のセルは、どの列も受け付けないセルにする。
    /// </summary>
    private static InputCell CellOf(IXLCell cell)
    {
        if (cell.HasFormula)
        {
            return new InputCell(InputCellType.Formula, string.Empty);
        }

        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Text => InputCell.Text(value.GetText()),
            XLDataType.Number => new InputCell(InputCellType.Number, Shown(cell), Number: RoundedNumber.Of(value.GetNumber())),
            XLDataType.DateTime => DateOf(value) is { } date
                ? new InputCell(InputCellType.Date, Shown(cell), Date: date)
                : new InputCell(InputCellType.Other, Unshowable),
            _ => new InputCell(InputCellType.Other, Shown(cell)),
        };
    }

    /// <summary>日付のセルの日時。日付にできない値(9999-12-31 より後など)なら null。</summary>
    private static DateTime? DateOf(XLCellValue value)
    {
        try
        {
            return value.GetDateTime();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>セルの、Excel で表示される文字。ClosedXML が表示の書式で表せない値(時刻の書式の大きすぎる数など)は、Unshowable とする。</summary>
    private static string Shown(IXLCell cell)
    {
        try
        {
            return cell.GetFormattedString(DisplayCulture);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return Unshowable;
        }
    }

    /// <summary>値のあるセルか。空のセルと空の文字のセルは値がなく、数式のセルは、計算した値の有無によらず値がある。</summary>
    private static bool HasValue(IXLCell cell) =>
        cell.HasFormula || !(cell.Value.IsBlank || (cell.Value.IsText && cell.Value.GetText().Length == 0));
}
