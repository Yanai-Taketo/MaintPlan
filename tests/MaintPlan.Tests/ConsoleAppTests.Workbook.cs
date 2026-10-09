using ClosedXML.Excel;
using MaintPlan.Cli;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>確認用コンソールで、入力のブック(.xlsx)を読むことと、結果をブックに書き出すことを確かめる。</summary>
public sealed partial class ConsoleAppTests
{
    private string OutputWorkbook => Path.Combine(OutputDirectory, "結果.xlsx");

    /// <summary>入力のブック(入力.xlsx)を置いた、例12と例13のケース。</summary>
    public static TheoryData<string> WorkbookCaseIds =>
        [.. TestCases.All.Where(HasWorkbook).Select(testCase => testCase.Id)];

    /// <summary>例12と例13の、確認用コンソールが書き出す表の期待値ファイル。</summary>
    public static TheoryData<string, string> WorkbookWrittenExpectedFiles =>
        [.. TestCases.All.Where(HasWorkbook).SelectMany(testCase => testCase.ExpectedFileNames
            .Where(fileName => WrittenKinds.Contains(KindNameOf(fileName)))
            .Select(fileName => (testCase.Id, fileName)))];

    /// <summary>8章の段7の完了の条件:CSV のフォルダとブックのどちらから読んでも、6つの表が同じになる。</summary>
    [Theory]
    [MemberData(nameof(WorkbookCaseIds))]
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

    /// <summary>読むのは .xlsx のブックだけ。ほかの拡張子のファイルは、引数の誤りとする。</summary>
    [Theory]
    [InlineData("入力.csv")]
    [InlineData("入力.xls")]
    [InlineData("入力.xlsm")]
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

    /// <summary>ブックとして開けないファイル(中身がブックでない、空)は、ファイル名を示して止める。</summary>
    [Theory]
    [InlineData("ブックではない文字")]
    [InlineData("")]
    public void Broken_workbook_is_reported_and_nothing_is_written(string content)
    {
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "入力.xlsx");
        File.WriteAllText(input, content);

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

    private static bool HasWorkbook(TestCase testCase) =>
        testCase.Id.StartsWith("例12/", StringComparison.Ordinal) || testCase.Id.StartsWith("例13/", StringComparison.Ordinal);

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
}
