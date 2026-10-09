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
/// 例01-04の入力の CSV からブックを作り、セルを ClosedXML で変えて保存してから読む。期待する文は、決まりの文・利用者が決めた文言・CSV と同じ文から決め、
/// 行は、変えた行の Excel の行番号(列名の行が1行目)を手で数える。変えていない行の行番号は、CSV のファイルの行番号と同じ。
/// 値を変えるセルは表示の書式を決めて(省けば標準)、表示される文字が値の普通の書き方になるようにする。
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
    [InlineData("作業明細", "開始日", "2027/03/01", "「2027/03/01」はYYYY-MM-DD の日付ではありません。")]
    [InlineData("実績", "年月", "2027-13", "「2027-13」はYYYY-MM の年月ではありません。")]
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
    [Fact]
    public void Numeric_cells_are_rounded_to_15_digits()
    {
        Edit(workbook =>
        {
            Set(workbook, "作業明細", 2, "作業日数", 0.111111111111111);
            Set(workbook, "作業明細", 3, "作業日数", 0.222222222222222);
            Set(workbook, "工事", 2, "ID", 0.333333333333333);
        });
        ReplaceStoredNumbers(new Dictionary<string, string>
        {
            ["0.111111111111111"] = (0.1 + 0.2).ToString("R", CultureInfo.InvariantCulture),
            ["0.222222222222222"] = "10.500000000000002",
            ["0.333333333333333"] = "1.0000000000000002",
        });

        // 丸める前の値が、そのままブックに保存されていること(テストの前提)
        Assert.Equal(0.30000000000000004, Number("作業明細", 2, "作業日数"));
        Assert.Equal(10.500000000000002, Number("作業明細", 3, "作業日数"));
        Assert.Equal(1.0000000000000002, Number("工事", 2, "ID"));

        var plan = PlanWorkbookReader.ReadWorkbook(workbookPath);

        Assert.Equal(3, plan.LaborLines[0].WorkDaysTenths);
        Assert.Equal(105, plan.LaborLines[1].WorkDaysTenths);
        Assert.Equal(1, plan.ConstructionWorks[0].Id);
    }

    /// <summary>
    /// 列に合わない数値のセルは、表示される文字で示す。数値を受け付ける列(整数・年度・金額・作業日数・文字)は、15桁に丸めた値を CSV と同じ決まりで確かめ、
    /// 丸めた絶対値が10の15乗以上なら16桁以上の数と示す。日付・真偽・選択の列は、数値のセルを受け付けない。書式は標準。
    /// </summary>
    [Theory]
    // 日付の列の数値のセル。文は決まり8の例のまま
    [InlineData("工事", "開始日", 46113d, "「46113」は YYYY-MM-DD の日付ではありません。")]
    [InlineData("作業明細", "作業日数", 10.25, "「10.25」は0以上の、小数点以下1桁までの数ではありません。")]
    [InlineData("作業明細", "人数", 1.5, "「1.5」は0以上の整数ではありません。")]
    [InlineData("予算年割", "予算額", 1.5, "「1.5」は桁区切りのない整数ではありません。")]
    [InlineData("予算年割", "年度", 27d, "「27」は西暦4桁の年度ではありません。")]
    // 整数の列は、0以上だけを受け付ける
    [InlineData("工事", "ID", -2d, "「-2」は0以上の整数ではありません。")]
    [InlineData("費用内訳", "工事ID", -2d, "「-2」は0以上の整数ではありません。")]
    [InlineData("作業明細", "人数", -2d, "「-2」は0以上の整数ではありません。")]
    [InlineData("人員区分", "表示順", -2d, "「-2」は0以上の整数ではありません。")]
    // 文字の列は、整数の数値のセルだけを受け付ける(利用者が決めた文言)
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
    public void Numeric_cell_that_does_not_fit_the_column_is_reported(string sheet, string column, double value, string message)
    {
        Put(sheet, 2, column, value);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>
    /// 列に合わない日付のセルは、表示される文字で示す。日付の列は時刻のある日付、年月の列は1日でないか時刻のある日付を受け付けない。
    /// 日付と年月の列の文は、数値のセル(46113)と同じく決まり8の例の形(「は」の後に空白)にする。時刻は、2進数で割り切れる時刻にする。
    /// </summary>
    [Theory]
    [InlineData("工事", "開始日", "2027-02-10 12:00", "yyyy-mm-dd hh:mm", "「2027-02-10 12:00」は YYYY-MM-DD の日付ではありません。")]
    [InlineData("実績", "年月", "2027-02-15 00:00", "yyyy-mm-dd", "「2027-02-15」は YYYY-MM の年月ではありません。")]
    [InlineData("実績", "年月", "2027-02-01 06:00", "yyyy-mm-dd hh:mm", "「2027-02-01 06:00」は YYYY-MM の年月ではありません。")]
    // 文字の列の日付のセル(利用者が決めた文言)
    [InlineData("工事", "管理番号", "2027-03-01 00:00", "yyyy-mm-dd", "「2027-03-01」は文字か整数ではありません。")]
    [InlineData("予算年割", "予算額", "2027-03-01 00:00", "yyyy-mm-dd", "「2027-03-01」は桁区切りのない整数ではありません。")]
    public void Date_cell_that_does_not_fit_the_column_is_reported(string sheet, string column, string dateTime, string format, string message)
    {
        Put(sheet, 2, column, DateTime.ParseExact(dateTime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), format);

        Assert.Equal($"シート「{sheet}」 2行目 列「{column}」: {message}", Assert.Single(Errors()));
    }

    /// <summary>真偽のセルは、真偽の列でも受け付けない。表示される文字は、保存したセルの GetFormattedString で求める(TRUE・FALSE の書き方を決めつけない)。</summary>
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
                $"シート「工事」 2行目 列「削除済み」: 「{Shown("工事", 2, "削除済み")}」は「はい」か「いいえ」ではありません。",
                $"シート「工事」 3行目 列「管理番号」: 「{Shown("工事", 3, "管理番号")}」は文字か整数ではありません。",
            ],
            Errors());
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

    /// <summary>ブックとして開けないファイルは、ファイル名を場所とする形の誤り1件にする(利用者が決めた文言)。</summary>
    [Fact]
    public void File_that_is_not_a_workbook_is_reported_with_its_name()
    {
        File.WriteAllText(workbookPath, "ID,管理番号\n1,例1\n");

        var read = PlanWorkbookReader.Read(workbookPath);

        Assert.Null(read.Plan);
        Assert.Equal("入力.xlsx: ブックとして読めません。", Assert.Single(read.Errors).Message);
        Assert.Equal("入力.xlsx: ブックとして読めません。", Assert.ThrowsAny<Exception>(() => PlanWorkbookReader.ReadWorkbook(workbookPath)).Message);
    }

    /// <summary>Excel で開いたままのブックも読める(ほかの書き込みを許す共有で開く)。</summary>
    [Fact]
    public void Workbook_opened_for_writing_elsewhere_can_be_read()
    {
        using var opened = new FileStream(workbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Empty(PlanWorkbookReader.Read(workbookPath).Errors);
    }

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

    /// <summary>保存したブックのセルの、表示される文字(ClosedXML の GetFormattedString)。</summary>
    private string Shown(string sheet, int row, string column)
    {
        using var workbook = new XLWorkbook(workbookPath);
        return Cell(workbook, sheet, row, column).GetFormattedString();
    }

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

    /// <summary>シートの row 行目の、1行目の列名が column の列のセル。</summary>
    private static IXLCell Cell(XLWorkbook workbook, string sheet, int row, string column)
    {
        var worksheet = workbook.Worksheet(sheet);
        var header = worksheet.Row(1).CellsUsed().Single(cell => cell.GetString() == column);
        return worksheet.Cell(row, header.Address.ColumnNumber);
    }
}
