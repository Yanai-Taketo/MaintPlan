using System.Globalization;
using ClosedXML.Excel;
using MaintPlan.Cli;
using MaintPlan.IO.Results;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>確認用コンソールで、入力のブック(.xlsx)を読むことと、結果をブックに書き出すことを確かめる。</summary>
public sealed partial class ConsoleAppTests
{
    private string OutputWorkbook => Path.Combine(OutputDirectory, "結果.xlsx");

    /// <summary>例12と例13の、確認用コンソールが書き出す表の期待値ファイル。</summary>
    public static TheoryData<string, string> WorkbookWrittenExpectedFiles =>
        [.. TestCases.All.Where(InputWorkbooks.HasWorkbook).SelectMany(testCase => testCase.ExpectedFileNames
            .Where(fileName => WrittenKinds.Contains(KindNameOf(fileName)))
            .Select(fileName => (testCase.Id, fileName)))];

    /// <summary>入力のブックを置いたケース(例12)の、未入力の件数の期待値ファイル。</summary>
    public static TheoryData<string, string> WorkbookMissingAmountExpectedFiles =>
        [.. TestCases.All.Where(InputWorkbooks.HasWorkbook).SelectMany(testCase => testCase.ExpectedFileNames
            .Where(fileName => KindNameOf(fileName) == MissingAmountKind)
            .Select(fileName => (testCase.Id, fileName)))];

    /// <summary>8章の段7の完了の条件:CSV のフォルダとブックのどちらから読んでも、6つの表が同じになる。</summary>
    [Theory]
    [MemberData(nameof(InputWorkbooks.CaseIds), MemberType = typeof(InputWorkbooks))]
    public void Workbook_and_folder_inputs_write_the_same_tables(string caseId)
    {
        var testCase = TestCases.Get(caseId);
        var folderOutput = Path.Combine(directory, "フォルダから");

        var folderRun = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Output, folderOutput));
        var workbookRun = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Input, testCase.WorkbookPath));

        Assert.Equal(ConsoleApp.Succeeded, folderRun.ExitCode);
        Assert.Equal(ConsoleApp.Succeeded, workbookRun.ExitCode);
        Assert.Equal(
            Directory.EnumerateFiles(folderOutput).Select(Path.GetFileName).Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(OutputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var kind in WrittenKinds)
        {
            Assert.Equal(File.ReadAllText(Path.Combine(folderOutput, kind + ".csv")), File.ReadAllText(Path.Combine(OutputDirectory, kind + ".csv")));
        }
    }

    [Theory]
    [MemberData(nameof(WorkbookWrittenExpectedFiles))]
    public void Table_written_from_workbook_matches_expected(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));

        var run = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Input, testCase.WorkbookPath));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        AssertMatches(expected, ReadWritten(expected.Kind.Name));
    }

    /// <summary>ブックに書き出した表(表の名前のシート)を読み直し、期待値ファイルと直に比べる。入力は CSV のフォルダ。</summary>
    [Theory]
    [MemberData(nameof(WorkbookWrittenExpectedFiles))]
    public void Sheet_of_output_workbook_matches_expected(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));

        var run = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Output, OutputWorkbook));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        AssertMatches(expected, ReadSheet(OutputWorkbook, expected.Kind.Name));
    }

    /// <summary>ブックから読んだときの画面の未入力の件数は、フォルダから読んだときと同じ行で、期待値と合う。</summary>
    [Theory]
    [MemberData(nameof(WorkbookMissingAmountExpectedFiles))]
    public void Shown_missing_amounts_are_the_same_for_workbook_and_folder_inputs(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));

        var folderRun = Run(ArgumentsOf(testCase));
        var workbookRun = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Input, testCase.WorkbookPath));

        Assert.Equal(ConsoleApp.Succeeded, folderRun.ExitCode);
        Assert.Equal(ConsoleApp.Succeeded, workbookRun.ExitCode);
        Assert.NotEmpty(MissingAmountLines(folderRun.Output));
        Assert.Equal(MissingAmountLines(folderRun.Output), MissingAmountLines(workbookRun.Output));
        AssertMatches(expected, ShownMissingAmounts(workbookRun.Output));
    }

    /// <summary>出力先の末尾が .xlsx(大文字と小文字は問わない)なら、CSV と同じ表を、表ごとのシートにしたブック1冊を書き出す。</summary>
    [Theory]
    [InlineData("例12/条件1", "結果.xlsx")]
    [InlineData("例13/基本", "結果.xlsx")]
    [InlineData("例12/条件1", "結果.XLSX")]
    public void Workbook_output_has_the_tables_of_folder_output(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var workbookFolder = Path.Combine(directory, "ブック");
        var workbookPath = Path.Combine(workbookFolder, fileName);

        var folderRun = Run(ArgumentsOf(testCase));
        var workbookRun = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Output, workbookPath));

        Assert.Equal(ConsoleApp.Succeeded, folderRun.ExitCode);
        Assert.Equal(ConsoleApp.Succeeded, workbookRun.ExitCode);
        Assert.Equal([fileName], Directory.EnumerateFileSystemEntries(workbookFolder).Select(Path.GetFileName));
        using var workbook = new XLWorkbook(workbookPath);
        Assert.Equal(WrittenKinds, workbook.Worksheets.Select(sheet => sheet.Name));
        foreach (var kind in WrittenKinds)
        {
            ResultSheets.AssertMatches(workbook.Worksheet(kind), kind, ReadWritten(kind));
        }
    }

    [Fact]
    public void Parent_folders_of_output_workbook_are_created()
    {
        var workbookPath = Path.Combine(directory, "結果", "2027年度", "結果.xlsx");

        var run = Run(WithValue(ArgumentsOf(TestCases.Get("例12/条件1")), ConsoleArguments.Output, workbookPath));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        Assert.True(File.Exists(workbookPath));
    }

    /// <summary>ブックは作り直して上書きする。ブックにあったほかのシートは残らない。</summary>
    [Fact]
    public void Output_workbook_is_rebuilt_without_other_sheets()
    {
        var args = WithValue(ArgumentsOf(TestCases.Get("例12/条件1")), ConsoleArguments.Output, OutputWorkbook);
        var first = Run([.. args, ConsoleArguments.AmountKindOption, "見積額"]);
        using (var workbook = new XLWorkbook(OutputWorkbook))
        {
            workbook.Worksheets.Add("メモ").Cell(1, 1).Value = "残らない";
            workbook.Save();
        }

        var second = Run(args);

        Assert.Equal(ConsoleApp.Succeeded, first.ExitCode);
        Assert.Equal(ConsoleApp.Succeeded, second.ExitCode);
        using var rebuilt = new XLWorkbook(OutputWorkbook);
        Assert.Equal(WrittenKinds, rebuilt.Worksheets.Select(sheet => sheet.Name));
        Assert.Equal(["欄", "予算額", "見積額", "実績額", "人工"], rebuilt.Worksheet("山積み").Row(1).CellsUsed().Select(cell => cell.GetString()));
    }

    [Fact]
    public void Nothing_is_written_when_a_folder_has_the_name_of_the_output_workbook()
    {
        Directory.CreateDirectory(OutputWorkbook);

        var run = Run(WithValue(ArgumentsOf(TestCases.Get("例12/条件1")), ConsoleArguments.Output, OutputWorkbook));

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal("書き出せません: 結果.xlsx と同じ名前のフォルダがあります。", Lines(run.Error)[0]);
        Assert.Equal(["結果.xlsx"], Directory.EnumerateFileSystemEntries(OutputDirectory).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFileSystemEntries(OutputWorkbook));
    }

    [Fact]
    public void Output_workbook_in_use_is_not_overwritten()
    {
        var args = WithValue(ArgumentsOf(TestCases.Get("例12/条件1")), ConsoleArguments.Output, OutputWorkbook);
        var first = Run([.. args, ConsoleArguments.AmountKindOption, "見積額"]);
        var written = File.ReadAllBytes(OutputWorkbook);

        (int ExitCode, string Output, string Error) second;
        using (new FileStream(OutputWorkbook, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            second = Run([.. args, ConsoleArguments.AmountKindOption, "予算額"]);
        }

        Assert.Equal(ConsoleApp.Succeeded, first.ExitCode);
        Assert.Equal(ConsoleApp.Failed, second.ExitCode);
        Assert.Equal("書き出せません: 結果.xlsx を開けません。ほかで開かれていれば、閉じてからやり直してください。", Lines(second.Error)[0]);
        Assert.Equal(written, File.ReadAllBytes(OutputWorkbook));
    }

    /// <summary>出力先に、ブックとして開けない「結果.xlsx」(文字のファイル)があっても、ブックを作り直して書く。</summary>
    [Fact]
    public void Output_file_that_is_not_a_workbook_is_replaced_with_a_workbook()
    {
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(OutputWorkbook, "ブックではない文字");

        var run = Run(WithValue(ArgumentsOf(TestCases.Get("例12/条件1")), ConsoleArguments.Output, OutputWorkbook));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        using var workbook = new XLWorkbook(OutputWorkbook);
        Assert.Equal(WrittenKinds, workbook.Worksheets.Select(sheet => sheet.Name));
    }

    /// <summary>出力先がブックでも、入力に誤りがあれば、ブックを作らない(出力先のフォルダも作らない)。</summary>
    [Fact]
    public void Nothing_is_written_to_output_workbook_when_input_has_an_error()
    {
        var input = CopyWorkbook("例12/条件1");
        using (var workbook = new XLWorkbook(input))
        {
            var sheet = workbook.Worksheet("予算年割");
            sheet.Cell(3, ColumnOf(sheet, "年度")).Value = "20x7";
            workbook.Save();
        }

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputWorkbook]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal("読み込めません: シート「予算年割」 3行目 列「年度」: 「20x7」は西暦4桁の年度ではありません。", Lines(run.Error)[0]);
        Assert.False(File.Exists(OutputWorkbook));
        Assert.False(Directory.Exists(OutputDirectory));
    }

    /// <summary>ブックに書き出したときは、書き出し先の後に、シート名を表の順に1行ずつ出す。</summary>
    [Fact]
    public void Sheet_names_are_shown_after_writing_a_workbook()
    {
        var run = Run(WithValue(ArgumentsOf(TestCases.Get("例12/条件1")), ConsoleArguments.Output, OutputWorkbook));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        Assert.Equal(
            [$"書き出し: {OutputWorkbook}", "  月ごとの値", "  予算額の初期値", "  山積み", "  人工の内訳", "  残予算と見込み残", "  計算に使わない修正"],
            Lines(run.Output.TrimEnd()).SkipWhile(line => line != $"書き出し: {OutputWorkbook}"));
    }

    /// <summary>CSV のフォルダに書き出したときは、今のとおり、書き出し先の後にファイル名を表の順に1行ずつ出す。</summary>
    [Fact]
    public void File_names_are_shown_after_writing_csv_files()
    {
        var run = Run(ArgumentsOf(TestCases.Get("例12/条件1")));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        Assert.Equal(
            [
                $"書き出し: {OutputDirectory}", "  月ごとの値.csv", "  予算額の初期値.csv", "  山積み.csv", "  人工の内訳.csv", "  残予算と見込み残.csv",
                "  計算に使わない修正.csv",
            ],
            Lines(run.Output.TrimEnd()).SkipWhile(line => line != $"書き出し: {OutputDirectory}"));
    }

    [Fact]
    public void Workbook_with_upper_case_extension_is_read()
    {
        var testCase = TestCases.Get("例12/条件1");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "入力.XLSX");
        File.Copy(testCase.WorkbookPath, input);

        var run = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Input, input));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        AssertMatches(ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, "山積み.csv")), ReadWritten("山積み"));
    }

    /// <summary>フォルダなら、名前の末尾が .xlsx でも CSV のフォルダとして読む。</summary>
    [Fact]
    public void Folder_named_like_a_workbook_is_read_as_csv_folder()
    {
        var testCase = TestCases.Get("例12/条件1");
        var input = Path.Combine(directory, "入力.xlsx");
        Directory.Move(CopyInput(testCase.Id), input);

        var run = Run(WithValue(ArgumentsOf(testCase), ConsoleArguments.Input, input));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        AssertMatches(ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, "山積み.csv")), ReadWritten("山積み"));
    }

    [Theory]
    [InlineData("ない.xlsx")]
    [InlineData("ない.XLSX")]
    public void Missing_input_workbook_is_reported(string fileName)
    {
        var input = Path.Combine(directory, fileName);

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.UsageError, run.ExitCode);
        Assert.Equal($"入力のブックがありません: {input}", Lines(run.Error)[0]);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    /// <summary>読むのは .xlsx のブックだけ。ほかの拡張子のファイルと、拡張子のないファイルは、引数の誤りとする。</summary>
    [Theory]
    [InlineData("入力.csv")]
    [InlineData("入力.xls")]
    [InlineData("入力.xlsm")]
    [InlineData("入力")]
    public void Input_file_that_is_not_a_workbook_is_reported(string fileName)
    {
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, fileName);
        File.WriteAllText(input, string.Empty);

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.UsageError, run.ExitCode);
        Assert.Equal($"入力には、CSV のフォルダか .xlsx のブックを指定します: {input}", Lines(run.Error)[0]);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Missing_input_without_workbook_extension_is_reported_as_missing_folder()
    {
        var input = Path.Combine(directory, "ない.csv");

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.UsageError, run.ExitCode);
        Assert.Equal($"入力のフォルダがありません: {input}", Lines(run.Error)[0]);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Workbook_violation_is_reported_with_sheet_and_row_and_nothing_is_written()
    {
        var input = CopyWorkbook("例12/条件1");
        using (var workbook = new XLWorkbook(input))
        {
            // 工事のシートは、列名の行と5件の工事(2〜6行目)。7行目に、2行目の工事を写して管理番号だけを替えた行を足す
            var sheet = workbook.Worksheet("工事");
            sheet.Cell(7, 1).CopyFrom(sheet.Range(2, 1, 2, sheet.Row(1).CellsUsed().Count()));
            sheet.Cell(7, ColumnOf(sheet, "管理番号")).Value = "例9";
            workbook.Save();
        }

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal(
            [
                "決まりに合いません: シート「工事」 7行目 列「ID」: ID 1 が重複しています。最初の行は2行目です。",
                "1件の誤りがあるため、表を書き出していません。",
            ],
            Lines(run.Error.TrimEnd()));
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Workbook_read_error_is_reported_with_sheet_and_row_and_nothing_is_written()
    {
        var input = CopyWorkbook("例12/条件1");
        using (var workbook = new XLWorkbook(input))
        {
            var sheet = workbook.Worksheet("予算年割");
            sheet.Cell(3, ColumnOf(sheet, "年度")).Value = "20x7";
            workbook.Save();
        }

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal(
            [
                "読み込めません: シート「予算年割」 3行目 列「年度」: 「20x7」は西暦4桁の年度ではありません。",
                "1件の誤りがあるため、表を書き出していません。",
            ],
            Lines(run.Error.TrimEnd()));
        Assert.False(Directory.Exists(OutputDirectory));
    }

    /// <summary>
    /// ブックから読んだ入力の保存できない条件も、シートと行で示す。設計書4章の例14の#9と同じ形の違反を、例12/条件1のブックに作る。
    /// 月別修正のシート(列名の行だけ)の2行目に、費用内訳1の2027-05の予算額の修正 1,000,000 を足す。
    /// 費用内訳1は工事1(工期2027-04-01〜2027-06-30)の出来高の費用内訳で、2027-05は工期に入る。
    /// その2027年度の年割は 900,000(予算年割のシートの2行目、ID 1)なので、2027年度の修正の合計 1,000,000 が年割額を超える。
    /// ほかの行は、変えていないケースのとおり決まりに合う。
    /// </summary>
    [Fact]
    public void Save_violation_read_from_workbook_is_reported_with_sheet_and_row()
    {
        var input = CopyWorkbook("例12/条件1");
        using (var workbook = new XLWorkbook(input))
        {
            var sheet = workbook.Worksheet("月別修正");
            sheet.Cell(2, ColumnOf(sheet, "ID")).Value = 1d;
            sheet.Cell(2, ColumnOf(sheet, "費用内訳ID")).Value = 1d;
            sheet.Cell(2, ColumnOf(sheet, "金額の種類")).Value = "予算額";
            var month = sheet.Cell(2, ColumnOf(sheet, "年月"));
            month.Value = new DateTime(2027, 5, 1);
            month.Style.NumberFormat.Format = InputWorkbooks.YearMonthFormat;
            sheet.Cell(2, ColumnOf(sheet, "金額")).Value = 1_000_000d;
            sheet.Cell(2, ColumnOf(sheet, "削除済み")).Value = "いいえ";
            workbook.Save();
        }

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal(
            [
                "保存できません: シート「予算年割」 2行目 列「予算額」: 2027年度の予算額の修正の合計 1,000,000 が、年割額 900,000 を超えています。",
                "1件の誤りがあるため、表を書き出していません。",
            ],
            Lines(run.Error.TrimEnd()));
        Assert.False(Directory.Exists(OutputDirectory));
    }

    /// <summary>
    /// 列が2つの違反は、ブックから読んだときも「列「費用内訳ID」「年度」」と並べて示す。
    /// 例12/条件1のブックの予算年割のシートは、列名の行と6件(2〜7行目、ID 1〜6)。8行目に、2行目(ID 1、費用内訳ID 1、2027年度)を写して
    /// ID だけを 7 にした行を足すと、費用内訳ID 1 と2027年度の組み合わせが、2行目と8行目で重複する。
    /// 重複した年割の行は保存の確認から除くので、ほかの違反は出ない。
    /// </summary>
    [Fact]
    public void Violation_of_two_columns_read_from_workbook_shows_both_columns()
    {
        var input = CopyWorkbook("例12/条件1");
        using (var workbook = new XLWorkbook(input))
        {
            var sheet = workbook.Worksheet("予算年割");
            sheet.Cell(8, 1).CopyFrom(sheet.Range(2, 1, 2, sheet.Row(1).CellsUsed().Count()));
            sheet.Cell(8, ColumnOf(sheet, "ID")).Value = 7d;
            workbook.Save();
        }

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal(
            [
                "決まりに合いません: シート「予算年割」 8行目 列「費用内訳ID」「年度」: 費用内訳ID 1 と年度 2027 の組み合わせが重複しています。最初の行は2行目です。",
                "1件の誤りがあるため、表を書き出していません。",
            ],
            Lines(run.Error.TrimEnd()));
        Assert.False(Directory.Exists(OutputDirectory));
    }

    /// <summary>
    /// ブックとして開けないファイル(文字・空・ブックでない zip・パスワード付きの形・途中で切れたもの)は、ファイル名を示して止める。
    /// 終了コードは1。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreadableWorkbooks.Kinds), MemberType = typeof(UnreadableWorkbooks))]
    public void Broken_workbook_is_reported_and_nothing_is_written(string kind)
    {
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "入力.xlsx");
        UnreadableWorkbooks.Write(kind, input);

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal("読み込めません: 入力.xlsx: ブックとして読めません。", Lines(run.Error)[0]);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    /// <summary>使い方の1行目と、--input・--output の行は、フォルダとブックの両方を示す。</summary>
    [Fact]
    public void Usage_shows_folder_or_workbook_for_input_and_output()
    {
        var lines = Lines(ConsoleArguments.Usage);

        Assert.Equal("使い方: MaintPlan.Cli --input <入力のフォルダかブック> --output <出力先のフォルダかブック> [集計の条件]", lines[0]);
        Assert.Contains("  --input        9つのテーブルの CSV を置いたフォルダか、9つのテーブルのシートを置いたブック(.xlsx)。決まりに合わない行があれば、行を示して書き出さない", lines);
        Assert.Contains("  --output       結果の表を書き出す先。末尾が .xlsx ならブック(表ごとのシート)に、それ以外はフォルダ(表ごとの CSV)に書き出す。なければ作り、同じ名前のファイルは上書きする", lines);
    }

    /// <summary>引数の、option の値を value に替える。</summary>
    private static string[] WithValue(string[] args, string option, string value)
    {
        var replaced = (string[])args.Clone();
        replaced[Array.IndexOf(replaced, option) + 1] = value;
        return replaced;
    }

    /// <summary>ケースの入力のブックを、一時フォルダの「入力.xlsx」に写す。</summary>
    private string CopyWorkbook(string caseId)
    {
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "入力.xlsx");
        File.Copy(TestCases.Get(caseId).WorkbookPath, input);
        return input;
    }

    /// <summary>シートの1行目で、列名が name のセルの列の番号。</summary>
    private static int ColumnOf(IXLWorksheet sheet, string name) =>
        sheet.Row(1).CellsUsed().Single(cell => cell.GetString() == name).Address.ColumnNumber;

    /// <summary>
    /// 書き出したブックの、sheetName のシートを表にする。1行目を列名とする。
    /// 数値のセルは数の文字(15 の人工は「15」。期待値との比較で「15.0」と同じとみなす)、文字のセルはその文字、空のセルは空の値にする。
    /// </summary>
    private static TextTable ReadSheet(string workbookPath, string sheetName)
    {
        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheet(sheetName);
        var columnCount = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var rowCount = sheet.LastRowUsed()?.RowNumber() ?? 0;
        List<string> RowOf(int row) => [.. Enumerable.Range(1, columnCount).Select(column => CellText(sheet.Cell(row, column)))];
        return new TextTable(RowOf(1), [.. Enumerable.Range(2, Math.Max(0, rowCount - 1)).Select(RowOf)]);
    }

    private static string CellText(IXLCell cell) => cell.DataType switch
    {
        XLDataType.Blank => string.Empty,
        XLDataType.Number => ((decimal)cell.GetDouble()).ToString(CultureInfo.InvariantCulture),
        XLDataType.Text => cell.GetText(),
        _ => throw new InvalidOperationException($"シート「{cell.Worksheet.Name}」の {cell.Address.ToStringRelative()} は、数値・文字・空のどれでもないセルです({cell.DataType})。"),
    };

    /// <summary>画面の「未入力の件数:」の行と、その下の字下げした行。</summary>
    private static List<string> MissingAmountLines(string output) =>
        [.. Lines(output).SkipWhile(line => line != "未入力の件数:").TakeWhile((line, index) => index == 0 || line.StartsWith(' '))];
}
