using System.Globalization;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Excel;
using MaintPlan.IO.Input;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>
/// 入力のブック(.xlsx)の読み込みで、引継ぎ資料の「Excel の読み書き」の決まり2〜9と、列名のない列の扱いのとおりに読み、
/// 形の違う値をシート・行・列とともに知らせることを確かめる。
/// 例01-04の入力の CSV からブックを作り、セルを ClosedXML で変えて保存してから読む。期待する文は、決まりの文・CSV と同じ文・ブックにだけある文(「シートがありません。」「文字か整数ではありません。」など)から決め、
/// 行は、変えた行の Excel の行番号(列名の行が1行目)を手で数える。変えていない行の行番号は、CSV のファイルの行番号と同じ。
/// 値を変えるセルは表示の書式を決めて(省けば標準)、表示される文字が値の普通の書き方になるようにする。
/// 表示される文字と値の違いを確かめるテスト(書式「#,##0」「0000」など)では、表示される文字を書式から手で決める。
/// 表示される文字は ja-JP で作る決まりなので、期待する文はマシンの文化によらない(Displayed_text_does_not_depend_on_the_current_culture)。
/// </summary>
public sealed class WorkbookReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    private readonly string workbookPath;

    public WorkbookReaderTests()
    {
        Directory.CreateDirectory(directory);
        workbookPath = Path.Combine(directory, "入力.xlsx");
        InputWorkbooks.Create(InputDirectory, workbookPath);
    }

    private static string InputDirectory => TestCases.Get("例01-04/基本").InputDirectory;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Sample_workbook_can_be_read()
    {
        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.NotNull(read.Plan);
        Assert.Equal(9, read.Source!.Tables.Count);
        var csv = PlanCsvReader.Read(InputDirectory);
        foreach (var table in Enum.GetValues<PlanTable>())
        {
            Assert.Equal(csv.Source!.Tables[table].LineNumbers, read.Source.Tables[table].LineNumbers);
        }
    }

    /// <summary>ブックは同じフォルダの CSV から作るので、読んだテーブルは行ごとに同じになる。</summary>
    [Fact]
    public void Workbook_gives_the_same_tables_as_the_csv_folder()
    {
        var fromWorkbook = PlanWorkbookReader.ReadWorkbook(workbookPath);
        var fromCsv = PlanCsvReader.ReadFolder(InputDirectory);

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

    /// <summary>テーブルと同じ名前でないシート(メモや、テーブルのシートの写し)は、読まない。</summary>
    [Fact]
    public void Other_sheets_are_not_read()
    {
        Edit(workbook =>
        {
            var memo = workbook.Worksheets.Add("メモ", 1);
            memo.Cell(1, 1).Value = "ID";
            memo.Cell(2, 1).FormulaA1 = "1+1";
            memo.Cell(3, 1).Value = XLError.DivisionByZero;
            var copy = workbook.Worksheet("工事").CopyTo("工事 (2)");
            copy.Cell(2, 1).Value = "いち";
        });

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.Equal(PlanCsvReader.ReadFolder(InputDirectory).ConstructionWorks, read.Plan!.ConstructionWorks);
    }

    /// <summary>列名に過不足があるテーブルと、シートがないテーブルの行は読まず、ほかのテーブルは読み続けて、誤りをすべて集める。</summary>
    [Fact]
    public void Sheet_problems_of_all_tables_are_collected()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 1, "備考", "メモ");
            Set(workbook, "予算年割", 3, "年度", "20x7");
            workbook.Worksheet("単価").Delete();
        });

        Assert.Equal(
            [
                "シート「工事」 1行目: 足りない列: 備考。知らない列: メモ",
                "シート「予算年割」 3行目 列「年度」: 「20x7」は西暦4桁の年度ではありません。",
                "シート「単価」: シートがありません。",
            ],
            Errors());
    }

    [Fact]
    public void Duplicate_column_names_are_reported()
    {
        Edit(workbook =>
        {
            var works = workbook.Worksheet("工事");
            works.Cell(1, works.LastColumnUsed()!.ColumnNumber() + 1).Value = "ID";
        });

        Assert.Equal(["シート「工事」 1行目: 列名が重複しています: ID"], Errors());
    }

    /// <summary>値のあるセルが1つもない行(空の行と、書式だけの行)は読まない。読んだ行の場所は、Excel の行番号のまま。</summary>
    [Fact]
    public void Rows_without_values_are_not_read()
    {
        Edit(workbook =>
        {
            // 3行目に空の行を、5行目に書式だけの行を入れる。工事1〜4は2・4・6・7行目になる
            var works = workbook.Worksheet("工事");
            works.Row(3).InsertRowsAbove(1);
            works.Row(5).InsertRowsAbove(1);
            works.Cell(5, 1).Style.Fill.BackgroundColor = XLColor.Yellow;
            works.Cell(5, 2).Style.NumberFormat.Format = "@";
        });

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.Equal([1, 2, 3, 4], read.Plan!.ConstructionWorks.Select(work => work.Id));
        Assert.Equal([2, 4, 6, 7], read.Source!.Tables[PlanTable.ConstructionWork].LineNumbers);
    }

    /// <summary>空の文字のセル("")は空欄なので、値のあるセルに数えない。空の文字のセルだけの行は、値のない行として読まない。</summary>
    [Fact]
    public void Rows_with_only_empty_text_cells_are_not_read()
    {
        Edit(workbook =>
        {
            // 3行目に行を入れ、ID・管理番号・状態・削除済みを空の文字のセルにする。工事2〜4は4〜6行目になる
            workbook.Worksheet("工事").Row(3).InsertRowsAbove(1);
            foreach (var column in new[] { "ID", "管理番号", "状態", "削除済み" })
            {
                Set(workbook, "工事", 3, column, string.Empty);
            }
        });

        // 空の文字のセルが、保存したブックに文字のセルとして残っていること(テストの前提)
        using (var saved = new XLWorkbook(workbookPath))
        {
            Assert.True(IsEmptyText(Cell(saved, "工事", 3, "管理番号")));
        }

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.Equal([1, 2, 3, 4], read.Plan!.ConstructionWorks.Select(work => work.Id));
        Assert.Equal([2, 4, 5, 6], read.Source!.Tables[PlanTable.ConstructionWork].LineNumbers);
    }

    [Fact]
    public void Malformed_value_is_shown_at_its_excel_row()
    {
        Edit(workbook =>
        {
            // 3行目に空の行を入れる。工事4は6行目になる
            workbook.Worksheet("工事").Row(3).InsertRowsAbove(1);
            Set(workbook, "工事", 6, "状態", "施工ちゅう");
        });

        Assert.Equal(["シート「工事」 6行目 列「状態」: 「施工ちゅう」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。"], Errors());
    }

    /// <summary>入力の決まりに合わない行も、Excel の行番号で示す。空の行を挟むので、行番号は一覧の順から数えた番号と違う。</summary>
    [Fact]
    public void Input_violation_is_shown_at_its_excel_row()
    {
        Edit(workbook =>
        {
            // 3行目に空の行を入れ(工事2〜4は4〜6行目になる)、7行目に工事2と同じ ID の削除済みの行を足す
            workbook.Worksheet("工事").Row(3).InsertRowsAbove(1);
            Set(workbook, "工事", 7, "ID", 2d);
            Set(workbook, "工事", 7, "管理番号", "例9");
            Set(workbook, "工事", 7, "工事名", "ポンプ更新");
            Set(workbook, "工事", 7, "状態", "施工中");
            Set(workbook, "工事", 7, "削除済み", "はい");
        });

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        var violation = Assert.Single(InputValidation.Validate(read.Plan!).Violations);
        Assert.Equal("シート「工事」 7行目 列「ID」: ID 2 が重複しています。最初の行は4行目です。", InputViolationText.Describe(violation, read.Source!));
    }

    /// <summary>文字のセルは、数や日付の列でも、CSV と同じ決まりで読む。</summary>
    [Fact]
    public void Text_cells_are_read_like_csv_values()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 2, "ID", "9");
            Set(workbook, "予算年割", 2, "予算額", "1600000");
            Set(workbook, "作業明細", 2, "作業日数", "8.5");
            Set(workbook, "作業明細", 2, "開始日", "2027-03-02");
            Set(workbook, "実績", 2, "年月", "2027-04");
        });

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Equal(9, plan.ConstructionWorks[0].Id);
        Assert.Equal(1_600_000, plan.AnnualBudgets[0].Amount);
        Assert.Equal(85, plan.LaborLines[0].WorkDaysTenths);
        Assert.Equal(new DateOnly(2027, 3, 2), plan.LaborLines[0].StartDate);
        Assert.Equal(new YearMonth(2027, 4), plan.ActualCosts[0].Month);
    }

    /// <summary>文字のセルの形の誤りは、CSV と同じ文で示す。</summary>
    [Theory]
    [InlineData("工事", "ID", "-2", "「-2」は0以上の整数ではありません。")]
    [InlineData("工事", "状態", "施工ちゅう", "「施工ちゅう」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。")]
    [InlineData("工事", "削除済み", "TRUE", "「TRUE」は「はい」か「いいえ」ではありません。")]
    [InlineData("予算年割", "年度", "20x7", "「20x7」は西暦4桁の年度ではありません。")]
    [InlineData("予算年割", "予算額", "1,603,076", "「1,603,076」は桁区切りのない整数ではありません。")]
    [InlineData("作業明細", "作業日数", "10.25", "「10.25」は0以上の、小数点以下1桁までの数ではありません。")]
    [InlineData("作業明細", "開始日", "2027/03/01", "「2027/03/01」は YYYY-MM-DD の日付ではありません。")]
    [InlineData("実績", "年月", "2027-13", "「2027-13」は YYYY-MM の年月ではありません。")]
    [InlineData("月別修正", "金額の種類", "実績額", "「実績額」は予算額・見積額のどれかではありません。")]
    public void Malformed_text_cell_is_reported_like_csv(string sheet, string column, string text, string message)
    {
        Put(sheet, 2, column, text);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>空のセルと空の文字のセルは空欄。空欄にできない列では、CSV と同じく空欄にできないと示す。</summary>
    [Theory]
    [InlineData("工事", "管理番号", false)]
    [InlineData("工事", "管理番号", true)]
    [InlineData("工事", "削除済み", true)]
    [InlineData("予算年割", "予算額", false)]
    [InlineData("予算年割", "予算額", true)]
    [InlineData("実績", "年月", true)]
    public void Empty_cell_in_required_column_is_reported(string sheet, string column, bool emptyText)
    {
        XLCellValue value = emptyText ? string.Empty : Blank.Value;
        Put(sheet, 2, column, value);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: 空欄にできません。", Assert.Single(Errors()));
    }

    [Fact]
    public void Empty_text_cell_in_optional_column_is_blank()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 2, "備考", string.Empty);
            Set(workbook, "工事", 2, "優先度", string.Empty);
            Set(workbook, "費用内訳", 2, "見積額", string.Empty);
        });

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Null(plan.ConstructionWorks[0].Remarks);
        Assert.Null(plan.ConstructionWorks[0].Priority);
        Assert.Null(plan.CostItems[0].EstimateAmount);
    }

    /// <summary>
    /// 文字のほかに受け付けるセル。文字の列の整数の数値はその数字の文字として、15桁までの数は数値のセルのまま読む。
    /// 日付の列は時刻が0の日付のセル、年月の列は1日の日付のセルを、表示の書式によらず読む。
    /// </summary>
    [Fact]
    public void Numeric_and_date_cells_are_read_in_columns_that_accept_them()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 2, "管理番号", 123456d);
            Set(workbook, "工事", 4, "実施年度", 2028d);
            Set(workbook, "費用内訳", 2, "工事番号", 7d);
            Set(workbook, "費用内訳", 2, "見積額", 999999999999999d);
            Set(workbook, "人員区分", 3, "表示順", 5d);
            Set(workbook, "作業明細", 2, "人数", 3d);
            Set(workbook, "作業明細", 2, "作業日数", 8.5);
            Set(workbook, "作業明細", 2, "開始日", new DateTime(2027, 3, 2), "yyyy/m/d");
            Set(workbook, "実績", 2, "年月", new DateTime(2027, 4, 1), "yyyy-mm-dd");
        });

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Equal("123456", plan.ConstructionWorks[0].ManagementNumber);
        Assert.Equal(2028, plan.ConstructionWorks[2].PlannedFiscalYear);
        Assert.Equal("7", plan.CostItems[0].WorkNumber);
        Assert.Equal(999_999_999_999_999, plan.CostItems[0].EstimateAmount);
        Assert.Equal(5, plan.StaffCategories[1].DisplayOrder);
        Assert.Equal(3, plan.LaborLines[0].Headcount);
        Assert.Equal(85, plan.LaborLines[0].WorkDaysTenths);
        Assert.Equal(new DateOnly(2027, 3, 2), plan.LaborLines[0].StartDate);
        Assert.Equal(new YearMonth(2027, 4), plan.ActualCosts[0].Month);
    }

    /// <summary>
    /// 数値のセルは、有効数字15桁に丸めてから読む。
    /// ClosedXML は保存するときに数を15桁に丸めるので、目印の数を入れて保存してから、シートの XML の値を丸める前の数(Excel が保存する17桁の形)に書き換える。
    /// </summary>
    /// <remarks>
    /// 文字の列(工事番号)の 123456.00000000001 は、15桁に丸めると整数の 123456 になるので「123456」と読む。
    /// 見積額の 999999999999999.4 は、15桁に丸めると 999999999999999 で、10の15乗より小さいので受け付ける(16桁以上の数の境目)。
    /// </remarks>
    [Fact]
    public void Numeric_cells_are_rounded_to_15_digits()
    {
        Edit(workbook =>
        {
            Set(workbook, "作業明細", 2, "作業日数", 0.111111111111111);
            Set(workbook, "作業明細", 3, "作業日数", 0.222222222222222);
            Set(workbook, "工事", 2, "ID", 0.333333333333333);
            Set(workbook, "費用内訳", 3, "見積額", 0.444444444444444);
            Set(workbook, "費用内訳", 2, "工事番号", 0.555555555555555);
        });
        ReplaceStoredNumbers(new Dictionary<string, string>
        {
            ["0.111111111111111"] = (0.1 + 0.2).ToString("R", CultureInfo.InvariantCulture),
            ["0.222222222222222"] = "10.500000000000002",
            ["0.333333333333333"] = "1.0000000000000002",
            ["0.444444444444444"] = "999999999999999.4",
            ["0.555555555555555"] = "123456.00000000001",
        });

        // 丸める前の値が、そのままブックに保存されていること(テストの前提)
        Assert.Equal(0.30000000000000004, Number("作業明細", 2, "作業日数"));
        Assert.Equal(10.500000000000002, Number("作業明細", 3, "作業日数"));
        Assert.Equal(1.0000000000000002, Number("工事", 2, "ID"));
        Assert.Equal(999999999999999.4, Number("費用内訳", 3, "見積額"));
        Assert.NotEqual(999999999999999d, Number("費用内訳", 3, "見積額"));
        Assert.Equal(123456.00000000001, Number("費用内訳", 2, "工事番号"));
        Assert.NotEqual(123456d, Number("費用内訳", 2, "工事番号"));

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Equal(3, plan.LaborLines[0].WorkDaysTenths);
        Assert.Equal(105, plan.LaborLines[1].WorkDaysTenths);
        Assert.Equal(1, plan.ConstructionWorks[0].Id);
        Assert.Equal(999_999_999_999_999, plan.CostItems[1].EstimateAmount);
        Assert.Equal("123456", plan.CostItems[0].WorkNumber);
    }

    /// <summary>
    /// 列に合わない数値のセルは、表示される文字で示す。数値を受け付ける列(整数・年度・金額・作業日数・文字)は、15桁に丸めた値を CSV と同じ決まりで確かめ、
    /// 丸めた絶対値が10の15乗以上なら16桁以上の数と示す。日付・年月・真偽・選択の列は、数値のセルを受け付けない。書式は標準。
    /// </summary>
    [Theory]
    // 日付・年月の列は、数値のセルを受け付けない。日付と年月の列の文は、CSV と同じく「は」の後に半角の空白を置く
    [InlineData("工事", "開始日", 46113d, "「46113」は YYYY-MM-DD の日付ではありません。")]
    [InlineData("実績", "年月", 46082d, "「46082」は YYYY-MM の年月ではありません。")]
    [InlineData("作業明細", "作業日数", 10.25, "「10.25」は0以上の、小数点以下1桁までの数ではありません。")]
    [InlineData("作業明細", "人数", 1.5, "「1.5」は0以上の整数ではありません。")]
    [InlineData("予算年割", "予算額", 1.5, "「1.5」は桁区切りのない整数ではありません。")]
    [InlineData("予算年割", "年度", 27d, "「27」は西暦4桁の年度ではありません。")]
    // 年度の列も、15桁に丸めた値が整数の数値のセルだけを受け付ける
    [InlineData("予算年割", "年度", 2027.5, "「2027.5」は西暦4桁の年度ではありません。")]
    // 整数の列は、0以上だけを受け付ける
    [InlineData("工事", "ID", -2d, "「-2」は0以上の整数ではありません。")]
    [InlineData("費用内訳", "工事ID", -2d, "「-2」は0以上の整数ではありません。")]
    [InlineData("作業明細", "人数", -2d, "「-2」は0以上の整数ではありません。")]
    [InlineData("人員区分", "表示順", -2d, "「-2」は0以上の整数ではありません。")]
    // 文字の列は、整数の数値のセルだけを受け付け、ほかは「文字か整数ではありません」と示す
    [InlineData("工事", "管理番号", 12.5, "「12.5」は文字か整数ではありません。")]
    // 真偽と選択項目は、文字のセルだけを受け付ける
    [InlineData("工事", "状態", 1d, "「1」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。")]
    [InlineData("工事", "削除済み", 0d, "「0」は「はい」か「いいえ」ではありません。")]
    // 16桁以上の数
    [InlineData("費用内訳", "見積額", 1E+15, "16桁以上の数は、文字のセルで入れてください。")]
    [InlineData("費用内訳", "見積額", -1E+15, "16桁以上の数は、文字のセルで入れてください。")]
    [InlineData("工事", "ID", 1E+15, "16桁以上の数は、文字のセルで入れてください。")]
    [InlineData("工事", "管理番号", 1E+15, "16桁以上の数は、文字のセルで入れてください。")]
    [InlineData("作業明細", "作業日数", 1E+15, "16桁以上の数は、文字のセルで入れてください。")]
    // 数値を受け付けない列(選択・真偽)では、16桁以上の数も型の誤りとして、表示される文字(標準の書式の 1E+15 は「1E+15」)で示す
    [InlineData("工事", "状態", 1E+15, "「1E+15」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。")]
    [InlineData("工事", "削除済み", -1E+15, "「-1E+15」は「はい」か「いいえ」ではありません。")]
    public void Numeric_cell_that_does_not_fit_the_column_is_reported(string sheet, string column, double value, string message)
    {
        Put(sheet, 2, column, value);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>
    /// 誤りの文の値は、セルの値ではなく表示される文字で示す。表示される文字は、書式から手で決めた
    /// (書式「0000」の 27 は「0027」、「0.00」の 1.5 は「1.50」、「"はい"」のような文字だけの書式の数は、その文字)。
    /// </summary>
    [Theory]
    // 受け付けた数値が、CSV と同じ確かめで誤りになるとき
    [InlineData("予算年割", "年度", 27d, "0000", "「0027」は西暦4桁の年度ではありません。")]
    // 整数の列の、整数でない数値のセル
    [InlineData("予算年割", "予算額", 1.5, "0.00", "「1.50」は桁区切りのない整数ではありません。")]
    // 真偽・選択の列は、文字のセルだけを受け付ける。数値のセルは、表示される文字が正しい値に見えても誤りにし、文は CSV と同じ形にする
    [InlineData("工事", "削除済み", 1d, "\"はい\"", "「はい」は「はい」か「いいえ」ではありません。")]
    [InlineData("工事", "状態", 1d, "\"施工中\"", "「施工中」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。")]
    public void Numeric_cell_with_display_format_is_reported_with_its_displayed_text(string sheet, string column, double value, string format, string message)
    {
        Put(sheet, 2, column, value, format);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>
    /// 受け付けた数値は、表示される文字ではなく、セルの値(15桁に丸めた値)から読む。
    /// 金額・作業日数・文字の列は、表示される文字(「1,603,076」「8.50」「123,456」)を CSV の決まりで読めば誤りになる書式にする。
    /// 文字の列の整数は、桁区切りや指数の形、「.0」を付けない数字の文字として読む(負の整数は「-」を付ける)。
    /// </summary>
    [Fact]
    public void Accepted_numeric_cells_are_read_by_value()
    {
        Edit(workbook =>
        {
            Set(workbook, "予算年割", 2, "予算額", 1603076d, "#,##0");
            Set(workbook, "作業明細", 2, "作業日数", 8.5, "0.00");
            Set(workbook, "工事", 4, "実施年度", 2028d, "0");
            Set(workbook, "工事", 2, "管理番号", 123456d, "#,##0");
            Set(workbook, "工事", 3, "管理番号", 123456789012345d);
            Set(workbook, "費用内訳", 2, "工事番号", -5d);
        });

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Equal(1_603_076, plan.AnnualBudgets[0].Amount);
        Assert.Equal(85, plan.LaborLines[0].WorkDaysTenths);
        Assert.Equal(2028, plan.ConstructionWorks[2].PlannedFiscalYear);
        Assert.Equal("123456", plan.ConstructionWorks[0].ManagementNumber);
        Assert.Equal("123456789012345", plan.ConstructionWorks[1].ManagementNumber);
        Assert.Equal("-5", plan.CostItems[0].WorkNumber);
    }

    /// <summary>
    /// 受け付けた値は、表示される文字によらない。ClosedXML 0.105.1 が表示される文字を作れない書式(「.0E+00」の 1 は DivideByZeroException)でも、
    /// 正しい ID の数値のセルは、CSV と同じに読む。
    /// </summary>
    [Fact]
    public void Accepted_cell_is_read_even_if_its_displayed_text_cannot_be_made()
    {
        Put("工事", 2, "ID", 1d, ".0E+00");

        // ClosedXML が、このセルの表示される文字を作れないこと(テストの前提)
        using (var saved = new XLWorkbook(workbookPath))
        {
            Assert.Throws<DivideByZeroException>(() => Cell(saved, "工事", 2, "ID").GetFormattedString(DisplayCulture));
        }

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.Equal(PlanCsvReader.ReadFolder(InputDirectory).ConstructionWorks, read.Plan!.ConstructionWorks);
    }

    /// <summary>
    /// 列に合わないセルで、表示される文字を作れないもの(指数の書式「0.00E+00」の 0 は OverflowException、「.0E+00」の 1 は DivideByZeroException)は、
    /// 例外の種類によらず、Excel が表示できない値と同じ「########」で示す。
    /// </summary>
    [Theory]
    [InlineData(0d, "0.00E+00")]
    [InlineData(1d, ".0E+00")]
    public void Rejected_cell_whose_displayed_text_cannot_be_made_is_shown_as_hashes(double value, string format)
    {
        Put("工事", 2, "状態", value, format);

        // ClosedXML が、このセルの表示される文字を作れないこと(テストの前提)
        using (var saved = new XLWorkbook(workbookPath))
        {
            Assert.ThrowsAny<ArithmeticException>(() => Cell(saved, "工事", 2, "状態").GetFormattedString(DisplayCulture));
        }

        Assert.Equal(["シート「工事」 2行目 列「状態」: 「########」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。"], Errors());
    }

    /// <summary>
    /// 16桁以上の数かは、15桁に丸めたあとの値で決める。保存された 999999999999999.9 は、丸める前は10の15乗より小さいが、
    /// 15桁に丸めると 1E+15 になるので、16桁以上の数とする。
    /// </summary>
    [Fact]
    public void Number_that_rounds_to_16_digits_is_reported()
    {
        Put("費用内訳", 2, "見積額", 0.444444444444444);
        ReplaceStoredNumbers(new Dictionary<string, string> { ["0.444444444444444"] = "999999999999999.9" });

        // 丸める前の値が、そのままブックに保存されていること(テストの前提)
        Assert.Equal(999999999999999.9, Number("費用内訳", 2, "見積額"));
        Assert.True(Number("費用内訳", 2, "見積額") < 1E+15);

        Assert.Equal(["シート「費用内訳」 2行目 列「見積額」: 16桁以上の数は、文字のセルで入れてください。"], Errors());
    }

    /// <summary>
    /// 列に合わない日付のセルは、表示される文字で示す。日付の列は時刻のある日付、年月の列は1日でないか時刻のある日付を受け付けない。
    /// 日付と年月の列の文は、CSV と同じく「は」の後に半角の空白を置く。時刻は、2進数で割り切れる時刻にする。
    /// </summary>
    [Theory]
    [InlineData("工事", "開始日", "2027-02-10 12:00", "yyyy-mm-dd hh:mm", "「2027-02-10 12:00」は YYYY-MM-DD の日付ではありません。")]
    [InlineData("実績", "年月", "2027-02-15 00:00", "yyyy-mm-dd", "「2027-02-15」は YYYY-MM の年月ではありません。")]
    [InlineData("実績", "年月", "2027-02-01 06:00", "yyyy-mm-dd hh:mm", "「2027-02-01 06:00」は YYYY-MM の年月ではありません。")]
    // 文字の列の日付のセルは、「文字か整数ではありません」と示す
    [InlineData("工事", "管理番号", "2027-03-01 00:00", "yyyy-mm-dd", "「2027-03-01」は文字か整数ではありません。")]
    [InlineData("予算年割", "予算額", "2027-03-01 00:00", "yyyy-mm-dd", "「2027-03-01」は桁区切りのない整数ではありません。")]
    // 選択の列は、文字のセルだけを受け付ける
    [InlineData("工事", "状態", "2027-03-01 00:00", "yyyy-mm-dd", "「2027-03-01」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。")]
    public void Date_cell_that_does_not_fit_the_column_is_reported(string sheet, string column, string dateTime, string format, string message)
    {
        Put(sheet, 2, column, DateTime.ParseExact(dateTime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), format);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>
    /// 日本語(ja-JP)の決まった日付の書式(ECMA-376 の番号 27〜31・34〜36・50〜58)のセルは、日付のセルとして読む。この書式は、書式の定義なしに番号だけで保存され、
    /// ClosedXML 0.105.1 は数値のセルとして読む。例12/条件1の入力のブックの写しで、日付の列(工事と作業明細の開始日・終了日)と年月の列(実績の年月)の
    /// 値のあるセルを番号だけの書式にして、CSV と同じ日付に読むことを確かめる。1904年の日付の仕組みのブックでも、同じ日付に読む。
    /// </summary>
    [Theory]
    [MemberData(nameof(JapaneseDateFormats))]
    public void Cells_with_japanese_built_in_date_formats_are_read_as_dates(int dateFormatId, int yearMonthFormatId, bool use1904DateSystem)
    {
        var testCase = TestCases.Get("例12/条件1");
        File.Copy(testCase.WorkbookPath, workbookPath, overwrite: true);
        (string Sheet, string Column, int FormatId)[] columns =
        [
            ("工事", "開始日", dateFormatId),
            ("工事", "終了日", dateFormatId),
            ("作業明細", "開始日", dateFormatId),
            ("作業明細", "終了日", dateFormatId),
            ("実績", "年月", yearMonthFormatId),
        ];
        Edit(workbook =>
        {
            workbook.Use1904DateSystem = use1904DateSystem;
            foreach (var (sheet, column, formatId) in columns)
            {
                foreach (var cell in ValueCells(workbook, sheet, column))
                {
                    cell.Style.NumberFormat.NumberFormatId = formatId;
                }
            }
        });

        // 番号だけの書式で保存され、ClosedXML が数値のセルとして読むこと(テストの前提)
        using (var saved = new XLWorkbook(workbookPath))
        {
            Assert.Equal(use1904DateSystem, saved.Use1904DateSystem);
            foreach (var (sheet, column, formatId) in columns)
            {
                var cells = ValueCells(saved, sheet, column);
                Assert.NotEmpty(cells);
                Assert.All(cells, cell =>
                {
                    Assert.Equal(XLDataType.Number, cell.DataType);
                    Assert.Equal(formatId, cell.Style.NumberFormat.NumberFormatId);
                    Assert.Equal(string.Empty, cell.Style.NumberFormat.Format);
                });
            }
        }

        var fromWorkbook = PlanWorkbookReader.ReadWorkbook(workbookPath);
        var fromCsv = PlanCsvReader.ReadFolder(testCase.InputDirectory);

        Assert.Equal(fromCsv.ConstructionWorks, fromWorkbook.ConstructionWorks);
        Assert.Equal(fromCsv.LaborLines, fromWorkbook.LaborLines);
        Assert.Equal(fromCsv.ActualCosts, fromWorkbook.ActualCosts);
    }

    /// <summary>
    /// 日本語の決まった書式のうち、時刻だけの書式(番号 32・33)の数値のセルは、日付のセルではない。日付の列では、型の誤りとしてそのセルを示す
    /// (文の形は日付の列の決まりの文で、ここでは場所だけを確かめる)。
    /// </summary>
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    public void Cells_with_japanese_built_in_time_formats_are_not_dates(int formatId)
    {
        Edit(workbook => Cell(workbook, "工事", 2, "開始日").Style.NumberFormat.NumberFormatId = formatId);

        var error = Assert.Single(PlanWorkbookReader.Read(workbookPath).Errors);

        Assert.Equal("シート「工事」", error.Place);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal("開始日", error.Column);
    }

    /// <summary>
    /// 日付として正しくない日付のセルは、日付・年月の列で、列に合わない型のセルとして示す。正しくないのは、1900年の日付の仕組みでシリアル値が1より小さいもの(0・負)と
    /// 60(実在しない 1900-02-29。時刻のある 60.5 も)、1904年の仕組みで0より小さいもの、9999-12-31 より後のもの(1900年の仕組みで 2958466 から、1904年の仕組みで 2957004 から)。
    /// 日付の書式のセルの表示される文字は、Excel と同じく、0 は「1900-01-00」、60 は「1900-02-29」。日本語の決まった日付の書式のセルは、
    /// ClosedXML 0.105.1 がこの書式を知らないので、表示される文字は標準の書式の数(0 は「0」)。負の値と 9999-12-31 より後の値は「########」で示す。
    /// ClosedXML 0.105.1 は、1900年の仕組みの 60 を 61 と同じ 1900-03-01 の日時として読み、1904年の仕組みの -1 を 1904-01-01 の日時として読むが、どちらも誤りにする。
    /// </summary>
    [Theory]
    // 日付の書式(yyyy-mm-dd)のセル
    [InlineData(false, false, 0d, "1900-01-00")]
    [InlineData(false, false, 0.5, "1900-01-00")]
    [InlineData(false, false, -1d, "########")]
    [InlineData(false, false, 60d, "1900-02-29")]
    [InlineData(false, false, 60.5, "1900-02-29")]
    [InlineData(false, false, 2958466d, "########")]
    [InlineData(false, true, -1d, "########")]
    // 日本語の決まった日付の書式(番号 31 と 55)のセル
    [InlineData(true, false, 0d, "0")]
    [InlineData(true, false, -1d, "########")]
    [InlineData(true, false, 60d, "60")]
    [InlineData(true, false, 2958466d, "########")]
    [InlineData(true, true, -1d, "########")]
    [InlineData(true, true, 2957004d, "########")]
    public void Date_cell_that_is_not_a_real_date_is_reported(bool japaneseFormat, bool use1904DateSystem, double serial, string shown)
    {
        PutSerials(japaneseFormat, use1904DateSystem, serial, serial);

        Assert.Equal(
            [
                $"シート「工事」 2行目 列「開始日」: 「{shown}」は YYYY-MM-DD の日付ではありません。",
                $"シート「実績」 2行目 列「年月」: 「{shown}」は YYYY-MM の年月ではありません。",
            ],
            Errors());
    }

    /// <summary>
    /// 日付として正しい日付の端のセルは読む。1900年の日付の仕組みの 1(1900-01-01)と 61(1900-03-01)、2958465(9999-12-31)、
    /// 1904年の仕組みの 0(1904-01-01)・59(1904-02-29)・61(1904-03-02)と 2957003(9999-12-31)。
    /// 年月の列は、1日のシリアル値(1904-02-01 は 31、1904-04-01 は 91、9999-12-01 は、1900年の仕組みで 2958435、1904年の仕組みで 2956973)。
    /// 1904年の仕組みの日付の書式のセルの 60(1904-03-01)は確かめない。ClosedXML 0.105.1 は、60 と 61 を同じ値にして読むので、61 と同じ日付になる。
    /// </summary>
    [Theory]
    // 日付の書式(yyyy-mm-dd)のセル
    [InlineData(false, false, 1d, "1900-01-01", 1d, "1900-01")]
    [InlineData(false, false, 61d, "1900-03-01", 61d, "1900-03")]
    [InlineData(false, false, 2958465d, "9999-12-31", 2958435d, "9999-12")]
    [InlineData(false, true, 0d, "1904-01-01", 0d, "1904-01")]
    [InlineData(false, true, 59d, "1904-02-29", 31d, "1904-02")]
    [InlineData(false, true, 61d, "1904-03-02", 91d, "1904-04")]
    [InlineData(false, true, 2957003d, "9999-12-31", 2956973d, "9999-12")]
    // 日本語の決まった日付の書式(番号 31 と 55)のセル
    [InlineData(true, false, 1d, "1900-01-01", 1d, "1900-01")]
    [InlineData(true, false, 61d, "1900-03-01", 61d, "1900-03")]
    [InlineData(true, false, 2958465d, "9999-12-31", 2958435d, "9999-12")]
    [InlineData(true, true, 0d, "1904-01-01", 0d, "1904-01")]
    [InlineData(true, true, 2957003d, "9999-12-31", 2956973d, "9999-12")]
    public void Date_cells_at_the_ends_of_real_dates_are_read(bool japaneseFormat, bool use1904DateSystem, double dateSerial, string date, double monthSerial, string month)
    {
        PutSerials(japaneseFormat, use1904DateSystem, dateSerial, monthSerial);

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Equal(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), plan.ConstructionWorks[0].StartDate);
        Assert.Equal(YearMonth.Of(DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)), plan.ActualCosts[0].Month);
    }

    /// <summary>真偽のセルは、真偽の列でも受け付けない。表示される文字は、Excel と同じ「TRUE」「FALSE」。</summary>
    [Fact]
    public void Boolean_cell_is_reported_with_its_displayed_text()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 2, "削除済み", true);
            Set(workbook, "工事", 3, "管理番号", false);
        });

        Assert.Equal(
            [
                "シート「工事」 2行目 列「削除済み」: 「TRUE」は「はい」か「いいえ」ではありません。",
                "シート「工事」 3行目 列「管理番号」: 「FALSE」は文字か整数ではありません。",
            ],
            Errors());
    }

    /// <summary>真偽のセルは、年度・日付の列でも受け付けない。</summary>
    [Theory]
    [InlineData("予算年割", "年度", true, "「TRUE」は西暦4桁の年度ではありません。")]
    // 日付の列の真偽のセル
    [InlineData("工事", "開始日", true, "「TRUE」は YYYY-MM-DD の日付ではありません。")]
    public void Boolean_cell_in_other_columns_is_reported(string sheet, string column, bool value, string message)
    {
        Put(sheet, 2, column, value);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>エラーのセルは、年度・選択の列でも受け付けない。</summary>
    [Theory]
    [InlineData("予算年割", "年度", "#N/A", "「#N/A」は西暦4桁の年度ではありません。")]
    [InlineData("工事", "状態", "#DIV/0!", "「#DIV/0!」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。")]
    public void Error_cell_in_other_columns_is_reported(string sheet, string column, string error, string message)
    {
        XLError value = error switch
        {
            "#N/A" => XLError.NoValueAvailable,
            "#DIV/0!" => XLError.DivisionByZero,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "知らないエラーです。"),
        };
        Put(sheet, 2, column, value);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    [Fact]
    public void Error_cell_is_reported_with_its_displayed_text()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 2, "工事名", XLError.IncompatibleValue);
            Set(workbook, "予算年割", 2, "予算額", XLError.DivisionByZero);
            Set(workbook, "作業明細", 2, "人数", XLError.NoValueAvailable);
        });

        Assert.Equal(
            [
                "シート「工事」 2行目 列「工事名」: 「#VALUE!」は文字か整数ではありません。",
                "シート「予算年割」 2行目 列「予算額」: 「#DIV/0!」は桁区切りのない整数ではありません。",
                "シート「作業明細」 2行目 列「人数」: 「#N/A」は0以上の整数ではありません。",
            ],
            Errors());
    }

    /// <summary>数式のセルは、計算した値が列に合っていても読まない。</summary>
    [Theory]
    [InlineData("工事", "ID", "0+1")]
    [InlineData("工事", "工事名", "\"ポンプ\"&\"更新\"")]
    [InlineData("工事", "開始日", "DATE(2027,2,10)")]
    [InlineData("工事", "削除済み", "\"いい\"&\"え\"")]
    [InlineData("予算年割", "予算額", "1603000+76")]
    [InlineData("実績", "年月", "DATE(2027,2,1)")]
    [InlineData("作業明細", "作業日数", "21/2")]
    public void Formula_cell_is_not_read_in_any_column(string sheet, string column, string formula)
    {
        Edit(workbook => Cell(workbook, sheet, 2, column).FormulaA1 = formula);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: 数式のセルは読みません。値で貼り付けてください。", Assert.Single(Errors()));
    }

    /// <summary>
    /// 数式のセルは、計算した値の有無によらず、値のあるセルに数える。数式のセルだけの行も読み、列名のある列を CSV と同じ列の順に読んで、
    /// 数式のセルと、空欄の必須の列(工事の ID・管理番号・工事名・状態・削除済み)を示す。
    /// ClosedXML 0.105.1 は、数式を入れて保存したセルに計算した値を保存しないので、この行のセルは、値のない数式のセルになる。
    /// </summary>
    [Fact]
    public void Row_with_only_a_formula_cell_is_read_and_reported()
    {
        Edit(workbook =>
        {
            // 3行目に行を入れ、備考にだけ数式を入れる。工事2〜4は4〜6行目になる
            workbook.Worksheet("工事").Row(3).InsertRowsAbove(1);
            Cell(workbook, "工事", 3, "備考").FormulaA1 = "\"メモ\"";
        });

        Assert.Equal(
            [
                "シート「工事」 3行目 列「ID」: 空欄にできません。",
                "シート「工事」 3行目 列「管理番号」: 空欄にできません。",
                "シート「工事」 3行目 列「工事名」: 空欄にできません。",
                "シート「工事」 3行目 列「状態」: 空欄にできません。",
                "シート「工事」 3行目 列「備考」: 数式のセルは読みません。値で貼り付けてください。",
                "シート「工事」 3行目 列「削除済み」: 空欄にできません。",
            ],
            Errors());
    }

    /// <summary>
    /// 1行目(列名の行)の数式のセルは、決まり7の誤りを1行目に示し、そのシートの行は読まない(2行目の形の違う値も示さない)。
    /// ほかのシートは読み続ける。
    /// </summary>
    [Fact]
    public void Formula_cell_in_header_row_is_reported_and_rows_of_the_sheet_are_not_read()
    {
        Edit(workbook =>
        {
            Set(workbook, "工事", 2, "状態", "施工ちゅう");
            Set(workbook, "予算年割", 3, "年度", "20x7");
            Cell(workbook, "工事", 1, "備考").FormulaA1 = "\"備考\"";
        });

        Assert.Equal(
            [
                "シート「工事」 1行目: 数式のセルは読みません。値で貼り付けてください。",
                "シート「予算年割」 3行目 列「年度」: 「20x7」は西暦4桁の年度ではありません。",
            ],
            Errors());
    }

    /// <summary>列名の行のセルは、表示される文字で比べる(真偽の true は「TRUE」、標準の書式の 2027 は「2027」)。</summary>
    [Theory]
    [InlineData(true, "TRUE")]
    [InlineData(2027d, "2027")]
    public void Header_cells_are_compared_by_their_displayed_text(object header, string shown)
    {
        XLCellValue value = header switch
        {
            bool flag => flag,
            double number => number,
            _ => throw new ArgumentOutOfRangeException(nameof(header), header, "真偽か数を渡します。"),
        };
        Put("工事", 1, "備考", value);

        Assert.Equal([$"シート「工事」 1行目: 足りない列: 備考。知らない列: {shown}"], Errors());
    }

    /// <summary>
    /// 表示される文字を作れない列名のセル(書式「.0E+00」の 5)は、Excel が表示できない値と同じ「########」として比べるので、知らない列になる。
    /// </summary>
    [Fact]
    public void Header_cell_whose_displayed_text_cannot_be_made_is_an_unknown_column()
    {
        Put("工事", 1, "備考", 5d, ".0E+00");

        // ClosedXML が、このセル(備考の列は N 列)の表示される文字を作れないこと(テストの前提)
        using (var saved = new XLWorkbook(workbookPath))
        {
            Assert.Throws<DivideByZeroException>(() => saved.Worksheet("工事").Cell("N1").GetFormattedString(DisplayCulture));
        }

        Assert.Equal(["シート「工事」 1行目: 足りない列: 備考。知らない列: ########"], Errors());
    }

    /// <summary>列名のない列(1行目が空のセルの列)に値のある行は、行ごとに形の誤り(列なし)にする。</summary>
    [Fact]
    public void Value_in_a_column_without_name_is_reported()
    {
        Edit(workbook =>
        {
            // B列に列名のない列を入れて3行目に値を入れ、列名のある最後の列(削除済み)の2つ右の列の5行目にも値を入れる
            var works = workbook.Worksheet("工事");
            works.Column(2).InsertColumnsBefore(1);
            var lastColumn = works.LastColumnUsed()!.ColumnNumber();
            works.Cell(3, 2).Value = "メモ";
            works.Cell(5, lastColumn + 2).Value = "済";
        });

        Assert.Equal(["シート「工事」 3行目: 列名のない列に値があります。", "シート「工事」 5行目: 列名のない列に値があります。"], Errors());
    }

    /// <summary>値のない、列名のない列は無視する。列名は、1行目の値のあるセルを左から順に取る。</summary>
    [Fact]
    public void Column_without_name_and_values_is_ignored()
    {
        Edit(workbook =>
        {
            var works = workbook.Worksheet("工事");
            works.Column(2).InsertColumnsBefore(1);
            works.Cell(4, 2).Style.Fill.BackgroundColor = XLColor.Yellow;
        });

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.Equal(PlanCsvReader.ReadFolder(InputDirectory).ConstructionWorks, read.Plan!.ConstructionWorks);
    }

    /// <summary>空の文字のセル("")は空欄なので、列名のない列にあっても、値として数えない。</summary>
    [Fact]
    public void Column_without_name_and_only_empty_text_cells_is_ignored()
    {
        Edit(workbook =>
        {
            var works = workbook.Worksheet("工事");
            works.Column(2).InsertColumnsBefore(1);
            works.Cell(3, 2).Value = string.Empty;
            works.Cell(5, 2).Value = string.Empty;
        });

        // 空の文字のセルが、保存したブックに文字のセルとして残っていること(テストの前提)
        using (var saved = new XLWorkbook(workbookPath))
        {
            Assert.True(IsEmptyText(saved.Worksheet("工事").Cell(3, 2)));
        }

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Empty(read.Errors);
        Assert.Equal(PlanCsvReader.ReadFolder(InputDirectory).ConstructionWorks, read.Plan!.ConstructionWorks);
    }

    /// <summary>
    /// 列名のない列にだけ値がある行も、列名のある列は今のとおり読む。その行の列名のない列の誤りを先に示し、
    /// 続けて、列名のある列の誤り(空欄の必須の列:工事の ID・管理番号・工事名・状態・削除済み)を CSV と同じ列の順に示す。
    /// 列名のない列(B列)は ID の列(A列)より右にあるが、列名のない列の誤りが先になる。
    /// </summary>
    [Fact]
    public void Row_with_a_value_only_in_a_column_without_name_is_read_and_reported()
    {
        Edit(workbook =>
        {
            // B列に列名のない列を入れ、工事4(5行目)の次の6行目には、その列にだけ値を入れる
            var works = workbook.Worksheet("工事");
            works.Column(2).InsertColumnsBefore(1);
            works.Cell(6, 2).Value = "メモ";
        });

        Assert.Equal(
            [
                "シート「工事」 6行目: 列名のない列に値があります。",
                "シート「工事」 6行目 列「ID」: 空欄にできません。",
                "シート「工事」 6行目 列「管理番号」: 空欄にできません。",
                "シート「工事」 6行目 列「工事名」: 空欄にできません。",
                "シート「工事」 6行目 列「状態」: 空欄にできません。",
                "シート「工事」 6行目 列「削除済み」: 空欄にできません。",
            ],
            Errors());
    }

    /// <summary>1行目に値のあるセルが1つもないシートは、CSV と同じ文で列名の行がないと示す。2行目からの行があってもなくても同じ。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sheet_without_header_row_is_reported(bool hasRows)
    {
        Edit(workbook =>
        {
            if (hasRows)
            {
                workbook.Worksheet("工事").Row(1).Clear();
            }
            else
            {
                workbook.Worksheet("工事").Delete();
                workbook.Worksheets.Add("工事", 1);
            }
        });

        Assert.Equal(["シート「工事」: 列名の行がありません。"], Errors());
    }

    /// <summary>形の違う値は、テーブルの順、行の順、行の中は CSV と同じ列の順に、すべて集める。ReadWorkbook は最初の1件を投げる。</summary>
    [Fact]
    public void All_malformed_values_are_collected_in_table_row_and_column_order()
    {
        Edit(workbook =>
        {
            Set(workbook, "予算年割", 2, "予算額", string.Empty);
            Set(workbook, "工事", 4, "状態", "施工ちゅう");
            Set(workbook, "工事", 4, "ID", 1.5);
            Set(workbook, "工事", 3, "管理番号", 12.5);
        });

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Null(read.Plan);
        Assert.Null(read.Source);
        Assert.Equal(
            [
                "シート「工事」 3行目 列「管理番号」: 「12.5」は文字か整数ではありません。",
                "シート「工事」 4行目 列「ID」: 「1.5」は0以上の整数ではありません。",
                "シート「工事」 4行目 列「状態」: 「施工ちゅう」は計画中・承認済み・発注済み・施工中・完了・中止のどれかではありません。",
                "シート「予算年割」 2行目 列「予算額」: 空欄にできません。",
            ],
            read.Errors.Select(error => error.Message));
        Assert.Equal(read.Errors[0].Message, Assert.ThrowsAny<Exception>(() => PlanWorkbookReader.ReadWorkbook(workbookPath)).Message);
    }

    /// <summary>
    /// ブックとして開けないファイル(文字・空・ブックでない zip・パスワード付きの形・途中で切れたもの)は、
    /// 例外の種類によらず、ファイル名を場所とする形の誤り1件「入力.xlsx: ブックとして読めません。」にする。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreadableWorkbooks.Kinds), MemberType = typeof(UnreadableWorkbooks))]
    public void File_that_is_not_a_workbook_is_reported_with_its_name(string kind)
    {
        UnreadableWorkbooks.Write(kind, workbookPath);

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Null(read.Plan);
        Assert.Null(read.Source);
        Assert.Equal("入力.xlsx: ブックとして読めません。", Assert.Single(read.Errors).Message);
        Assert.Equal("入力.xlsx: ブックとして読めません。", Assert.ThrowsAny<Exception>(() => PlanWorkbookReader.ReadWorkbook(workbookPath)).Message);
    }

    /// <summary>
    /// Excel で開いたままのブックも読める(ほかの書き込みを許す共有で開く)。Windows でだけ動かす。
    /// ほかの OS では、.NET はほかのハンドルの共有の指定をほとんど確かめず(FileShare.None のときだけ排他のロックを取る)、
    /// 書き込みを許さない共有で開く実装でも読めてしまうので、確かめにならない。
    /// </summary>
    [Fact]
    public void Workbook_opened_for_writing_elsewhere_can_be_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows でだけ確かめられる。ほかの OS では、書き込みを許さない共有で開いても読めてしまう。");

        using var opened = new FileStream(workbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Empty(PlanWorkbookReader.Read(workbookPath).Errors);
    }

    /// <summary>
    /// 表示される文字は、マシンの文化(カルチャ)によらず日本語(ja-JP)で作る。小数点が「,」の de-DE や en-US で動かしても、
    /// 文の値は ja-JP の書き方(小数点は「.」、桁区切りは「,」)になる。
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Displayed_text_does_not_depend_on_the_current_culture(string cultureName)
    {
        Edit(workbook =>
        {
            Set(workbook, "作業明細", 2, "作業日数", 10.25);
            Set(workbook, "予算年割", 2, "予算額", 1234.5, "#,##0.0");
        });

        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        List<string> errors;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            errors = Errors();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }

        Assert.Equal(
            [
                "シート「予算年割」 2行目 列「予算額」: 「1,234.5」は桁区切りのない整数ではありません。",
                "シート「作業明細」 2行目 列「作業日数」: 「10.25」は0以上の、小数点以下1桁までの数ではありません。",
            ],
            errors);
    }

    /// <summary>日付の列の書式の番号、年月の列の書式の番号、1904年の日付の仕組みのブックにするか。</summary>
    public static TheoryData<int, int, bool> JapaneseDateFormats =>
    [
        (31, 55, false),
        (31, 55, true),
        .. new[] { 27, 28, 29, 30, 31, 34, 35, 36, 50, 51, 52, 53, 54, 55, 56, 57, 58 }.Select(id => (id, id, false)),
    ];

    /// <summary>表示される文字を作る文化。読み込みと同じ日本語(ja-JP)。</summary>
    private static CultureInfo DisplayCulture => CultureInfo.GetCultureInfo("ja-JP");

    private List<string> Errors() => [.. PlanWorkbookReader.Read(workbookPath).Errors.Select(error => error.Message)];

    /// <summary>ブックを開いて edit で変え、上書き保存する。</summary>
    private void Edit(Action<XLWorkbook> edit)
    {
        using var workbook = new XLWorkbook(workbookPath);
        edit(workbook);
        workbook.Save();
    }

    /// <summary>
    /// 保存したブックのシートの XML で、数値のセルの値の文字 stored(キー)を、値の文字(丸める前の数)に書き換える。
    /// どの stored も、ちょうど1つのセルにあること。
    /// </summary>
    private void ReplaceStoredNumbers(IReadOnlyDictionary<string, string> replacements)
    {
        var counts = replacements.Keys.ToDictionary(stored => stored, _ => 0);
        using (var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update))
        {
            var sheets = archive.Entries
                .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
                .ToList();
            foreach (var entry in sheets)
            {
                string xml;
                using (var reader = new StreamReader(entry.Open()))
                {
                    xml = reader.ReadToEnd();
                }

                var replaced = xml;
                foreach (var (stored, value) in replacements)
                {
                    var marker = $">{stored}</";
                    counts[stored] += (replaced.Length - replaced.Replace(marker, string.Empty, StringComparison.Ordinal).Length) / marker.Length;
                    replaced = replaced.Replace(marker, $">{value}</", StringComparison.Ordinal);
                }

                if (replaced != xml)
                {
                    using var stream = entry.Open();
                    stream.SetLength(0);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(replaced);
                }
            }
        }

        Assert.All(counts, count => Assert.True(count.Value == 1, $"「{count.Key}」の値のセル: {count.Value}個"));
    }

    /// <summary>1つのセルを変えて保存する。</summary>
    private void Put(string sheet, int row, string column, XLCellValue value, string? format = null) =>
        Edit(workbook => Set(workbook, sheet, row, column, value, format));

    /// <summary>
    /// ブックを、use1904DateSystem なら1904年の日付の仕組みにし、工事の2行目の開始日を dateSerial、実績の2行目の年月を monthSerial のシリアル値の数値のセルにして保存する。
    /// 書式は、japaneseFormat なら日本語の決まった日付の書式(開始日は番号 31、年月は番号 55)、そうでなければ日付の書式(yyyy-mm-dd)にする。
    /// 保存したセルを、ClosedXML が日付の書式のセルは日付のセルとして、日本語の決まった書式のセルは数値のセルとして読み、
    /// 1904年の仕組みの日付の書式のセルのほかは、保存したシリアル値をそのままセルの値として持つことを確かめる(テストの前提)。
    /// </summary>
    private void PutSerials(bool japaneseFormat, bool use1904DateSystem, double dateSerial, double monthSerial)
    {
        (string Sheet, string Column, double Serial, int FormatId)[] cells = [("工事", "開始日", dateSerial, 31), ("実績", "年月", monthSerial, 55)];
        Edit(workbook =>
        {
            workbook.Use1904DateSystem = use1904DateSystem;
            foreach (var (sheet, column, serial, formatId) in cells)
            {
                var cell = Cell(workbook, sheet, 2, column);
                cell.Value = serial;
                if (japaneseFormat)
                {
                    cell.Style.NumberFormat.NumberFormatId = formatId;
                }
                else
                {
                    cell.Style.NumberFormat.Format = InputWorkbooks.DateFormat;
                }
            }
        });

        using var saved = new XLWorkbook(workbookPath);
        Assert.Equal(use1904DateSystem, saved.Use1904DateSystem);
        foreach (var (sheet, column, serial, formatId) in cells)
        {
            var cell = Cell(saved, sheet, 2, column);
            Assert.Equal(japaneseFormat ? XLDataType.Number : XLDataType.DateTime, cell.DataType);
            if (japaneseFormat)
            {
                Assert.Equal(formatId, cell.Style.NumberFormat.NumberFormatId);
            }

            if (japaneseFormat || !use1904DateSystem)
            {
                Assert.Equal(serial, cell.Value.GetUnifiedNumber());
            }
        }
    }

    /// <summary>セルが、空の文字("")の文字のセルか。</summary>
    private static bool IsEmptyText(IXLCell cell) => cell.Value.IsText && cell.Value.GetText().Length == 0;

    /// <summary>保存したブックのセルの、数値の値。</summary>
    private double Number(string sheet, int row, string column)
    {
        using var workbook = new XLWorkbook(workbookPath);
        return Cell(workbook, sheet, row, column).Value.GetNumber();
    }

    /// <summary>
    /// シートの row 行目の、column の列のセルを value にし、表示の書式を format にする。format を省くと標準の書式にする。
    /// 日付の値には、format を必ず渡す(標準の書式の日付は、数値のセルになる)。
    /// </summary>
    private static void Set(XLWorkbook workbook, string sheet, int row, string column, XLCellValue value, string? format = null)
    {
        var cell = Cell(workbook, sheet, row, column);
        cell.Value = value;
        if (format is null)
        {
            cell.Style.NumberFormat.NumberFormatId = 0;
        }
        else
        {
            cell.Style.NumberFormat.Format = format;
        }
    }

    /// <summary>シートの、1行目の列名が column の列の、2行目からの値のあるセル。</summary>
    private static List<IXLCell> ValueCells(XLWorkbook workbook, string sheet, string column) =>
    [
        .. workbook.Worksheet(sheet).Column(Cell(workbook, sheet, 1, column).Address.ColumnNumber)
            .CellsUsed(XLCellsUsedOptions.Contents)
            .Where(cell => cell.Address.RowNumber > 1),
    ];

    /// <summary>シートの row 行目の、1行目の列名が column の列のセル。</summary>
    private static IXLCell Cell(XLWorkbook workbook, string sheet, int row, string column)
    {
        var worksheet = workbook.Worksheet(sheet);
        var header = worksheet.Row(1).CellsUsed().Single(cell => cell.GetString() == column);
        return worksheet.Cell(row, header.Address.ColumnNumber);
    }
}
