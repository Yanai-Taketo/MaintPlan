using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Excel;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>
/// 例12・例13の各ケースに置いた入力のブック(入力.xlsx)が、引継ぎ資料の「Excel の読み書き」の決まり14のとおり、
/// 「入力」フォルダの CSV と同じ中身であることを確かめる。ブックの作り直し方は tests/data/README.md にある。
/// </summary>
public sealed class InputWorkbookTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    public InputWorkbookTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>セルの種類の表は、9つのテーブルをテーブルの順に持ち、列は CSV の列と同じ順に持つ。</summary>
    [Fact]
    public void Cell_kinds_are_given_for_every_csv_column()
    {
        Assert.Equal(Enum.GetValues<PlanTable>().Select(Labels.Of), InputWorkbooks.Sheets.Select(sheet => sheet.Table));
        foreach (var sheet in InputWorkbooks.Sheets)
        {
            Assert.Equal(PlanCsvReader.TableColumns[sheet.Table], sheet.Columns.Select(column => column.Name));
        }
    }

    /// <summary>
    /// 置いたブックのセル(シート名、値か書式のあるセルの値・型・書式・数式)は、今の「入力」フォルダの CSV から作ったブックと同じ。
    /// ファイルのバイトは、作るたびに作成日時などが変わるので比べない。
    /// </summary>
    [Theory]
    [MemberData(nameof(InputWorkbooks.CaseIds), MemberType = typeof(InputWorkbooks))]
    public void Committed_workbook_has_the_same_cells_as_one_made_from_the_csv(string caseId)
    {
        var testCase = TestCases.Get(caseId);
        var made = Path.Combine(directory, TestCase.WorkbookFileName);
        InputWorkbooks.Create(testCase.InputDirectory, made);

        using var expected = new XLWorkbook(made);
        using var committed = new XLWorkbook(testCase.WorkbookPath);

        Assert.Equal(expected.Worksheets.Select(sheet => sheet.Name), committed.Worksheets.Select(sheet => sheet.Name));
        foreach (var sheet in expected.Worksheets)
        {
            Assert.Equal(Cells(sheet), Cells(committed.Worksheet(sheet.Name)));
        }
    }

    /// <summary>
    /// セルの型と書式は、決まり14のとおり。値は、各ケースの「入力」フォルダの CSV の最初の行から手で写した。
    /// 例12の3ケースと例13の3ケースは、それぞれ同じ行で始まる。
    /// </summary>
    [Theory]
    [InlineData("例12/条件1", "例12A", "2027-04-01", "2027-09")]
    [InlineData("例12/条件2", "例12A", "2027-04-01", "2027-09")]
    [InlineData("例12/条件3", "例12A", "2027-04-01", "2027-09")]
    [InlineData("例13/基本", "例13A", "2027-02-01", "2027-03")]
    [InlineData("例13/例13Gを追加", "例13A", "2027-02-01", "2027-03")]
    [InlineData("例13/集計基準日2028-04-01", "例13A", "2027-02-01", "2027-03")]
    public void Cells_follow_the_workbook_rules(string caseId, string managementNumber, string startDate, string month)
    {
        using var workbook = new XLWorkbook(TestCases.Get(caseId).WorkbookPath);
        var work = workbook.Worksheet("工事");

        Assert.Equal(["工事", "費用内訳", "予算年割", "実績", "月別修正", "作業明細", "人員区分", "単価", "予算枠"], workbook.Worksheets.Select(sheet => sheet.Name));

        // 列名の行は、文字のセル。
        Assert.Equal(XLDataType.Text, work.Cell(1, 1).DataType);
        Assert.Equal("ID", work.Cell(1, 1).GetText());

        // 整数(ID)と金額(単価):数値のセルで、書式は標準。
        var id = CellOf(work, 2, "ID");
        Assert.Equal(XLDataType.Number, id.DataType);
        Assert.Equal(1, id.GetDouble());
        AssertGeneralFormat(id);
        var rate = CellOf(workbook.Worksheet("単価"), 2, "単価");
        Assert.Equal(XLDataType.Number, rate.DataType);
        Assert.Equal(30000, rate.GetDouble());

        // 文字列(管理番号):文字のセルで、書式は文字。空欄の長文(備考)は、値のない空のセルで、書式は文字。
        var number = CellOf(work, 2, "管理番号");
        Assert.Equal(XLDataType.Text, number.DataType);
        Assert.Equal(managementNumber, number.GetText());
        AssertTextFormat(number);
        var remarks = CellOf(work, 2, "備考");
        Assert.Equal(XLDataType.Blank, remarks.DataType);
        AssertTextFormat(remarks);

        // 選択(状態)と真偽(削除済み):文字のセルで、書式は標準。例12A・例13A はどちらも施工中。
        var status = CellOf(work, 2, "状態");
        Assert.Equal(XLDataType.Text, status.DataType);
        Assert.Equal("施工中", status.GetText());
        AssertGeneralFormat(status);
        var deleted = CellOf(work, 2, "削除済み");
        Assert.Equal(XLDataType.Text, deleted.DataType);
        Assert.Equal("いいえ", deleted.GetText());
        AssertGeneralFormat(deleted);

        // 空欄の年度(実施年度):値のない空のセルで、書式は標準。
        var plannedYear = CellOf(work, 2, "実施年度");
        Assert.Equal(XLDataType.Blank, plannedYear.DataType);
        AssertGeneralFormat(plannedYear);

        // 日付(開始日):時刻が0の日付のセルで、書式は yyyy-mm-dd。表示される文字は CSV と同じ。
        var start = CellOf(work, 2, "開始日");
        Assert.Equal(XLDataType.DateTime, start.DataType);
        Assert.Equal(DateOnly.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), start.GetDateTime());
        Assert.Equal("yyyy-mm-dd", start.Style.NumberFormat.Format);
        Assert.Equal(startDate, start.GetFormattedString(CultureInfo.InvariantCulture));

        // 年月(実績の年月):その月の1日の、時刻が0の日付のセルで、書式は yyyy-mm。表示される文字は CSV と同じ。
        var actualMonth = CellOf(workbook.Worksheet("実績"), 2, "年月");
        Assert.Equal(XLDataType.DateTime, actualMonth.DataType);
        Assert.Equal(DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), actualMonth.GetDateTime());
        Assert.Equal("yyyy-mm", actualMonth.Style.NumberFormat.Format);
        Assert.Equal(month, actualMonth.GetFormattedString(CultureInfo.InvariantCulture));
    }

    /// <summary>作業日数(小数点以下1桁の数)は、数値のセル。例12の作業明細の ID 1 は 9.1、ID 2 は「3.0」で 3 になる。</summary>
    [Fact]
    public void Work_days_are_number_cells()
    {
        using var workbook = new XLWorkbook(TestCases.Get("例12/条件1").WorkbookPath);
        var labor = workbook.Worksheet("作業明細");

        var first = CellOf(labor, 2, "作業日数");
        var second = CellOf(labor, 3, "作業日数");

        Assert.Equal(XLDataType.Number, first.DataType);
        Assert.Equal(9.1, first.GetDouble());
        AssertGeneralFormat(first);
        Assert.Equal(XLDataType.Number, second.DataType);
        Assert.Equal(3, second.GetDouble());
    }

    /// <summary>8章の段7の完了の条件:ブックから読んだ9つのテーブルは、「入力」フォルダの CSV から読んだものと行ごとに同じ。</summary>
    [Theory]
    [MemberData(nameof(InputWorkbooks.CaseIds), MemberType = typeof(InputWorkbooks))]
    public void Workbook_gives_the_same_tables_as_the_input_folder(string caseId)
    {
        var testCase = TestCases.Get(caseId);

        var fromWorkbook = PlanWorkbookReader.ReadWorkbook(testCase.WorkbookPath);
        var fromCsv = PlanCsvReader.ReadFolder(testCase.InputDirectory);

        Assert.Equal(fromCsv.ConstructionWorks, fromWorkbook.ConstructionWorks);
        Assert.Equal(fromCsv.CostItems, fromWorkbook.CostItems);
        Assert.Equal(fromCsv.AnnualBudgets, fromWorkbook.AnnualBudgets);
        Assert.Equal(fromCsv.ActualCosts, fromWorkbook.ActualCosts);
        Assert.Equal(fromCsv.MonthlyOverrides, fromWorkbook.MonthlyOverrides);
        Assert.Equal(fromCsv.LaborLines, fromWorkbook.LaborLines);
        Assert.Equal(fromCsv.StaffCategories, fromWorkbook.StaffCategories);
        Assert.Equal(fromCsv.UnitRates, fromWorkbook.UnitRates);
        Assert.Equal(fromCsv.BudgetFrames, fromWorkbook.BudgetFrames);
    }

    /// <summary>
    /// CSV から作ったブックの文字のセルは、CSV の値と同じ。先頭が「'」の管理番号と、「'」だけの工事名も「'」を残し、ブックから CSV と同じに読む。
    /// </summary>
    [Fact]
    public void Created_workbook_keeps_a_leading_apostrophe()
    {
        var input = Path.Combine(directory, "入力");
        Directory.CreateDirectory(input);
        foreach (var file in Directory.EnumerateFiles(TestCases.Get("例01-04/基本").InputDirectory))
        {
            File.Copy(file, Path.Combine(input, Path.GetFileName(file)));
        }

        // 工事1の管理番号「例1」を「'例1」に、工事名「ポンプ更新」を「'」にする
        var works = Path.Combine(input, "工事.csv");
        var text = File.ReadAllText(works);
        Assert.Contains("\n1,例1,ポンプ更新,", text, StringComparison.Ordinal);
        File.WriteAllText(works, text.Replace("\n1,例1,ポンプ更新,", "\n1,'例1,',", StringComparison.Ordinal), new UTF8Encoding(true));
        var workbookPath = Path.Combine(directory, TestCase.WorkbookFileName);

        InputWorkbooks.Create(input, workbookPath);

        var fromCsv = PlanCsvReader.ReadFolder(input);
        Assert.Equal("'例1", fromCsv.ConstructionWorks[0].ManagementNumber);
        Assert.Equal("'", fromCsv.ConstructionWorks[0].Name);
        Assert.Equal(fromCsv.ConstructionWorks, PlanWorkbookReader.ReadWorkbook(workbookPath).ConstructionWorks);
    }

    /// <summary>
    /// 環境変数 MAINTPLAN_WRITE_INPUT_WORKBOOKS が 1 のときだけ、リポジトリの tests/data にある6冊を、CSV から作り直す。
    /// ほかのときはスキップする。
    /// </summary>
    [Fact]
    public void Rewrite_input_workbooks_in_the_source_tree()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(InputWorkbooks.RewriteVariable) == "1",
            $"入力のブックを作り直すときだけ、環境変数 {InputWorkbooks.RewriteVariable} を 1 にして動かす(tests/data/README.md)。");

        var written = InputWorkbooks.RewriteSourceWorkbooks();

        // 決まり14:例12・例13の各ケースに1冊ずつ、6冊。
        Assert.Equal(6, written.Count);
        Assert.All(written, path => Assert.True(File.Exists(path), path));
    }

    /// <summary>列名の行で列を探し、その列の row 行目のセルを返す。</summary>
    private static IXLCell CellOf(IXLWorksheet sheet, int row, string column) =>
        sheet.Cell(row, sheet.Row(1).CellsUsed().Single(cell => cell.GetText() == column).Address.ColumnNumber);

    /// <summary>書式が標準(General)。</summary>
    private static void AssertGeneralFormat(IXLCell cell)
    {
        var format = cell.Style.NumberFormat;
        Assert.True(
            format.NumberFormatId == (int)XLPredefinedFormat.Number.General && string.IsNullOrEmpty(format.Format),
            $"{cell.Address.ToStringRelative()} の書式: {format.NumberFormatId}「{format.Format}」");
    }

    /// <summary>書式が文字(@)。ClosedXML は、決まった書式の番号(49)で持つこともあるので、どちらも受け付ける。</summary>
    private static void AssertTextFormat(IXLCell cell)
    {
        var format = cell.Style.NumberFormat;
        Assert.True(
            format.Format == "@" || format.NumberFormatId == (int)XLPredefinedFormat.Number.Text,
            $"{cell.Address.ToStringRelative()} の書式: {format.NumberFormatId}「{format.Format}」");
    }

    /// <summary>シートの、値か書式のあるセルを、「番地 型 値 書式の番号 書式 数式」の文字にして、行と列の順に並べる。</summary>
    private static List<string> Cells(IXLWorksheet sheet) =>
    [
        .. sheet.CellsUsed(XLCellsUsedOptions.All)
            .OrderBy(cell => cell.Address.RowNumber)
            .ThenBy(cell => cell.Address.ColumnNumber)
            .Select(cell =>
                $"{cell.Address.ToStringRelative()} {cell.DataType} 「{ValueText(cell.Value)}」 " +
                $"{cell.Style.NumberFormat.NumberFormatId} 「{cell.Style.NumberFormat.Format}」 「{(cell.HasFormula ? cell.FormulaA1 : string.Empty)}」"),
    ];

    private static string ValueText(XLCellValue value) => value.Type switch
    {
        XLDataType.Blank => string.Empty,
        XLDataType.Boolean => value.GetBoolean() ? "TRUE" : "FALSE",
        XLDataType.Number => value.GetNumber().ToString("R", CultureInfo.InvariantCulture),
        XLDataType.Text => value.GetText(),
        XLDataType.Error => value.GetError().ToString(),
        XLDataType.DateTime => value.GetDateTime().ToString("O", CultureInfo.InvariantCulture),
        XLDataType.TimeSpan => value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Type, "知らないセルの型です。"),
    };
}
