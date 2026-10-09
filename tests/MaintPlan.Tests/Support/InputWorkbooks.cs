using System.Globalization;
using ClosedXML.Excel;
using MaintPlan.IO.Csv;

namespace MaintPlan.Tests.Support;

/// <summary>入力のブックのセルの種類。設計書3章の項目の型から決める。</summary>
public enum InputCellKind
{
    /// <summary>整数・金額・年度・小数。数値のセルで、書式は標準。</summary>
    Number,

    /// <summary>日付。日付のセルで、書式は yyyy-mm-dd。</summary>
    Date,

    /// <summary>年月。その月の1日の日付のセルで、書式は yyyy-mm。</summary>
    YearMonth,

    /// <summary>文字列・長文。文字のセルで、書式は文字(@)。値が空欄のセルも書式を文字にする。</summary>
    Text,

    /// <summary>選択・真偽。文字のセルで、書式は標準。</summary>
    Choice,
}

/// <summary>入力のブックの列と、そのセルの種類。</summary>
public sealed record InputColumn(string Name, InputCellKind Kind);

/// <summary>入力のブックのシート(テーブル)と、その列。列は CSV と同じ順。</summary>
public sealed record InputSheet(string Table, IReadOnlyList<InputColumn> Columns)
{
    public InputCellKind KindOf(string column) =>
        Columns.FirstOrDefault(candidate => candidate.Name == column)?.Kind
        ?? throw new InvalidOperationException($"テーブル「{Table}」の列「{column}」のセルの種類が決まっていません。");
}

/// <summary>
/// 例12・例13の各ケースに置く「入力.xlsx」を、「入力」フォルダの CSV から作る(引継ぎ資料の「Excel の読み書き」の決まり14)。
/// テーブルごとのシート(シート名はテーブル名、テーブルの順)の1行目に列名を、2行目から CSV の行を同じ順に置く。
/// セルの型は、列の型で決める(InputCellKind)。空欄は値のない空のセルにする。
/// </summary>
public static class InputWorkbooks
{
    /// <summary>1 のとき、リポジトリの tests/data にあるブックを作り直す環境変数。</summary>
    public const string RewriteVariable = "MAINTPLAN_WRITE_INPUT_WORKBOOKS";

    public const string DateFormat = "yyyy-mm-dd";

    public const string YearMonthFormat = "yyyy-mm";

    public const string TextFormat = "@";

    /// <summary>入力のブックを置く例。</summary>
    public static IReadOnlyList<string> Examples { get; } = ["例12", "例13"];

    /// <summary>9つのテーブルの、列ごとのセルの種類。設計書3章の項目の型による。削除済みは真偽。</summary>
    public static IReadOnlyList<InputSheet> Sheets { get; } =
    [
        new("工事",
        [
            Number("ID"), Text("管理番号"), Text("工事名"), Choice("状態"), Date("開始日"), Date("終了日"), Number("実施年度"),
            Text("設備名"), Text("設置場所"), Text("担当者"), Text("担当部署"), Text("協力会社名"), Choice("優先度"), Text("備考"), Choice("削除済み"),
        ]),
        new("費用内訳",
        [
            Number("ID"), Number("工事ID"), Choice("費用区分"), Choice("計上の考え方"), Text("工事番号"), Number("見積額"), Number("その他の費用"),
            Text("その他の費用の内容"), Choice("削除済み"),
        ]),
        new("予算年割", [Number("ID"), Number("費用内訳ID"), Number("年度"), Number("予算額"), Choice("削除済み")]),
        new("実績", [Number("ID"), Number("費用内訳ID"), YearMonth("年月"), Number("実績額"), Text("摘要"), Choice("削除済み")]),
        new("月別修正", [Number("ID"), Number("費用内訳ID"), Choice("金額の種類"), YearMonth("年月"), Number("金額"), Choice("削除済み")]),
        new("作業明細",
        [
            Number("ID"), Number("費用内訳ID"), Number("人員区分ID"), Text("内容"), Date("開始日"), Date("終了日"), Number("人数"), Number("作業日数"),
            Choice("削除済み"),
        ]),
        new("人員区分", [Number("ID"), Choice("種別"), Text("区分名"), Choice("予算額の計算に含める"), Number("表示順"), Choice("使用中"), Choice("削除済み")]),
        new("単価", [Number("ID"), Number("人員区分ID"), Number("年度"), Number("単価"), Choice("削除済み")]),
        new("予算枠", [Number("ID"), Number("年度"), Choice("費用区分"), Number("予算枠額"), Choice("削除済み")]),
    ];

    /// <summary>入力のブックを置いた、例12と例13のケース。</summary>
    public static TheoryData<string> CaseIds => [.. TestCases.All.Where(HasWorkbook).Select(testCase => testCase.Id)];

    public static bool HasWorkbook(TestCase testCase) =>
        Examples.Any(example => testCase.Id.StartsWith(example + "/", StringComparison.Ordinal));

    /// <summary>inputDirectory にある9つの CSV から、ブックを作って workbookPath に保存する。</summary>
    public static void Create(string inputDirectory, string workbookPath)
    {
        using var workbook = new XLWorkbook();

        // ClosedXML は作成者に OS のユーザー名を入れる。リポジトリに入れるブックに残さないよう、空にする。
        workbook.Properties.Author = string.Empty;
        foreach (var sheet in Sheets)
        {
            var table = CsvTable.Read(Path.Combine(inputDirectory, sheet.Table + ".csv"));
            var kinds = table.Columns.Select(sheet.KindOf).ToList();
            var worksheet = workbook.Worksheets.Add(sheet.Table);
            for (var column = 0; column < table.Columns.Count; column++)
            {
                worksheet.Cell(1, column + 1).Value = TextValue(table.Columns[column]);
            }

            for (var row = 0; row < table.Rows.Count; row++)
            {
                for (var column = 0; column < table.Columns.Count; column++)
                {
                    Write(worksheet.Cell(row + 2, column + 1), kinds[column], table.Rows[row].Cells[column]);
                }
            }
        }

        workbook.SaveAs(workbookPath);
    }

    /// <summary>
    /// リポジトリの tests/data(出力のフォルダの写しではない)にある、例12・例13の各ケースの「入力.xlsx」を、
    /// そのケースの「入力」フォルダの CSV から作り直す。作り直したブックのパスを返す。
    /// </summary>
    public static IReadOnlyList<string> RewriteSourceWorkbooks()
    {
        var dataRoot = SourceDataRoot();
        var written = new List<string>();
        foreach (var example in Examples)
        {
            foreach (var caseDirectory in Directory.EnumerateDirectories(Path.Combine(dataRoot, example)).Order(StringComparer.Ordinal))
            {
                var workbookPath = Path.Combine(caseDirectory, TestCase.WorkbookFileName);
                Create(Path.Combine(caseDirectory, "入力"), workbookPath);
                written.Add(workbookPath);
            }
        }

        return written;
    }

    /// <summary>リポジトリの tests/data。テストの出力のフォルダから、上のフォルダへたどって探す。</summary>
    public static string SourceDataRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"{AppContext.BaseDirectory} とその上のフォルダに、tests/data がありません。");
    }

    /// <summary>CSV の値を、列の種類のセルにする。日付と年月は時刻0の日付にする。</summary>
    private static void Write(IXLCell cell, InputCellKind kind, string text)
    {
        if (kind == InputCellKind.Text)
        {
            cell.Style.NumberFormat.Format = TextFormat;
        }

        if (text.Length == 0)
        {
            return;
        }

        // 日付の書式は、値を入れた後に決める(ClosedXML は、日付を入れたときに書式を変えることがあるため)。
        switch (kind)
        {
            case InputCellKind.Number:
                // CSV の数は decimal で読み、いちばん近い double を数値のセルに入れる(Excel に同じ数を打ち込んだときと同じ値)。
                cell.Value = (double)decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                break;
            case InputCellKind.Date:
                cell.Value = DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None).ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                break;
            case InputCellKind.YearMonth:
                cell.Value = DateOnly.ParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None).ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = YearMonthFormat;
                break;
            default:
                cell.Value = TextValue(text);
                break;
        }
    }

    /// <summary>
    /// 文字のセルに入れる値。ClosedXML 0.105.1 は、セルに入れた文字の先頭の「'」を1つ除いて、セルの印(quotePrefix)にする。
    /// 先頭が「'」の文字は「'」を1つ足して入れ、セルの文字を CSV の値と同じにする。
    /// </summary>
    private static string TextValue(string text) => text.StartsWith('\'') ? "'" + text : text;

    private static InputColumn Number(string name) => new(name, InputCellKind.Number);

    private static InputColumn Date(string name) => new(name, InputCellKind.Date);

    private static InputColumn YearMonth(string name) => new(name, InputCellKind.YearMonth);

    private static InputColumn Text(string name) => new(name, InputCellKind.Text);

    private static InputColumn Choice(string name) => new(name, InputCellKind.Choice);
}
