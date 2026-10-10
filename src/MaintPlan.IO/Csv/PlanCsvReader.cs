using MaintPlan.Core.Model;
using MaintPlan.IO.Input;

namespace MaintPlan.IO.Csv;

/// <summary>
/// 計算に使う9つのテーブルを、テーブルごとの CSV ファイル(「工事.csv」など)から読む。
/// 列名は設計書3章の項目名と「削除済み」とし、過不足があれば読まない。読み方はブックと同じ(PlanTableReader)。
/// </summary>
public static class PlanCsvReader
{
    public const string Deleted = PlanTableReader.Deleted;

    /// <summary>テーブル名と、その CSV の列名。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> TableColumns => PlanTableReader.TableColumns;

    /// <summary>
    /// フォルダにある9つの CSV を読む。形の違う値は、最初の1件で止めずにすべて集める。
    /// 形の違う値がなければ、読んだ内容と、行のファイルでの場所を返す。
    /// </summary>
    public static PlanReadResult Read(string folderPath) => PlanTableReader.Read(table => ReadTable(folderPath, table));

    /// <summary>フォルダにある9つの CSV を読む。形の違う値があれば、最初の1件を投げる。</summary>
    public static PlanData ReadFolder(string folderPath)
    {
        var result = Read(folderPath);
        return result.Plan ?? throw result.Errors[0];
    }

    /// <summary>テーブルの CSV を、文字のセルの表にする。ファイルがないときと、CSV の形が正しくないときは、CsvFormatException を投げる。</summary>
    private static InputTable ReadTable(string folderPath, PlanTable table)
    {
        var filePath = Path.Combine(folderPath, Labels.Of(table) + ".csv");
        if (!File.Exists(filePath))
        {
            throw new CsvFormatException(filePath, null, null, "ファイルがありません。");
        }

        var csvTable = CsvTable.Read(filePath);
        return new InputTable(
            filePath,
            Path.GetFileName(filePath),
            csvTable.Columns,
            [.. csvTable.Rows.Select(row => new InputRow(row.LineNumber, [.. row.Cells.Select(InputCell.Text)], []))],
            (lineNumber, column, message) => new CsvFormatException(filePath, lineNumber, column, message));
    }
}
