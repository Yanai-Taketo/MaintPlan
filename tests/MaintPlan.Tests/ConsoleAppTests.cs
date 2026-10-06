using System.Globalization;
using System.Text.RegularExpressions;
using MaintPlan.Cli;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Results;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>確認用コンソールを動かし、書き出したファイルと画面の表示を、期待値と比べる。</summary>
public sealed partial class ConsoleAppTests : IDisposable
{
    /// <summary>
    /// 集計基準日を省いたときの当日。ケース.csv に集計基準日がないケースでは、比べる表(残予算と見込み残以外)にこの日付は効かない。
    /// </summary>
    private static readonly DateOnly Today = new(2026, 10, 6);

    /// <summary>確認用コンソールが書き出す表。ファイル名は、期待値の種類の名前と同じ。</summary>
    private static readonly string[] WrittenKinds = ["月ごとの値", "予算額の初期値", "山積み", "人工の内訳", "残予算と見込み残", "計算に使わない修正"];

    private const string MissingAmountKind = "未入力の件数";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    private string OutputDirectory => Path.Combine(directory, "出力");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>確認用コンソールが書き出す表の期待値ファイル。</summary>
    public static TheoryData<string, string> WrittenExpectedFiles =>
        [.. TestCases.All.SelectMany(testCase => testCase.ExpectedFileNames
            .Where(fileName => WrittenKinds.Contains(KindNameOf(fileName)))
            .Select(fileName => (testCase.Id, fileName)))];

    /// <summary>未入力の件数の期待値ファイル。</summary>
    public static TheoryData<string, string> MissingAmountExpectedFiles =>
        [.. TestCases.All.SelectMany(testCase => testCase.ExpectedFileNames
            .Where(fileName => KindNameOf(fileName) == MissingAmountKind)
            .Select(fileName => (testCase.Id, fileName)))];

    [Theory]
    [MemberData(nameof(WrittenExpectedFiles))]
    public void Written_table_matches_expected(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));

        var run = Run(ArgumentsOf(testCase));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        AssertMatches(expected, ReadWritten(expected.Kind.Name));
    }

    [Theory]
    [MemberData(nameof(MissingAmountExpectedFiles))]
    public void Shown_missing_amounts_match_expected(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));

        var run = Run(ArgumentsOf(testCase));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        var shown = ShownMissingAmounts(run.Output);
        Assert.Equal(["予算額", "見積額"], shown.Rows.Select(row => row[0]));
        AssertMatches(expected, shown);
    }

    [Fact]
    public void All_tables_are_written_with_their_columns()
    {
        var columns = new Dictionary<string, string[]>
        {
            ["月ごとの値"] = ["例", "費用内訳", "年月", "予算額", "見積額", "実績額", "人工"],
            ["予算額の初期値"] = ["例", "費用内訳", "労務費", "予算額の初期値", "年割の初期値", "知らせる内容"],
            ["山積み"] = ["欄", "予算額", "見積額", "実績額", "人工"],
            ["人工の内訳"] = ["欄", "人員区分", "人工"],
            ["残予算と見込み残"] = ["年度・費用区分", "予算枠", "実績額", "残予算", "未実績見込み", "見込み残"],
            ["計算に使わない修正"] = ["例", "費用内訳", "金額の種類", "年月", "金額", "理由"],
        };

        var run = Run(ArgumentsOf(TestCases.Get("例12/条件1")));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        Assert.Equal(
            WrittenKinds.Select(kind => kind + ".csv").Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(OutputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var kind in WrittenKinds)
        {
            Assert.Equal(columns[kind], ReadWritten(kind).Columns);
        }
    }

    /// <summary>人工の内訳の種別ごとの行(直営・協力会社)を欄ごとに足すと、同じ条件の山積みの人工になる。</summary>
    [Theory]
    [InlineData("例12/条件1")]
    [InlineData("例12/条件2")]
    [InlineData("例12/条件3")]
    public void Labor_breakdown_follows_aggregation_condition(string caseId)
    {
        var testCase = TestCases.Get(caseId);
        var expected = Narrow(ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, "山積み.csv")), ["欄", "人工"]);

        var run = Run(ArgumentsOf(testCase));

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        var kindTotals = ReadWritten("人工の内訳").Rows
            .Where(row => row[1] is "直営" or "協力会社")
            .GroupBy(row => row[0])
            .Select(group => (IReadOnlyList<string>)[group.Key, TenthsText(group.Sum(row => Tenths(row[2])))])
            .ToList();
        AssertMatches(expected, new TextTable(["欄", "人工"], kindTotals));
    }

    [Theory]
    [InlineData("予算額", new[] { "予算額" })]
    [InlineData("見積額", new[] { "見積額" })]
    [InlineData("実績額", new string[0])]
    public void Amount_kind_limits_aggregation_to_that_kind(string amountKind, string[] shownMissingKinds)
    {
        var testCase = TestCases.Get("例12/条件1");
        var columns = new[] { "欄", amountKind, "人工" };
        var expected = Narrow(ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, "山積み.csv")), columns);

        var run = Run([.. ArgumentsOf(testCase), ConsoleArguments.AmountKindOption, amountKind]);

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        var written = ReadWritten("山積み");
        Assert.Equal(columns, written.Columns);
        AssertMatches(expected, written);
        Assert.Equal(shownMissingKinds, ShownMissingAmounts(run.Output).Rows.Select(row => row[0]));
        if (shownMissingKinds.Length == 0)
        {
            Assert.DoesNotContain("未入力の件数:", Lines(run.Output));
        }

        Assert.Contains($"  金額の種類: {amountKind}", Lines(run.Output));
    }

    [Fact]
    public void Omitted_conditions_use_all_statuses_both_categories_and_today()
    {
        var aggregationCase = TestCases.Get("例12/条件1");
        var remainingCase = TestCases.Get("例13/基本");

        var aggregationRun = Run([ConsoleArguments.Input, aggregationCase.InputDirectory, ConsoleArguments.Output, OutputDirectory]);
        var aggregation = ReadWritten("山積み");
        var remainingRun = Run([ConsoleArguments.Input, remainingCase.InputDirectory, ConsoleArguments.Output, OutputDirectory], remainingCase.BaseDate);
        var remaining = ReadWritten("残予算と見込み残");

        Assert.Equal(ConsoleApp.Succeeded, aggregationRun.ExitCode);
        AssertMatches(ExpectedTable.Load(Path.Combine(aggregationCase.ExpectedDirectory, "山積み.csv")), aggregation);
        Assert.Contains("  金額の種類: 予算額、見積額、実績額", Lines(aggregationRun.Output));
        Assert.Contains("  含める状態: 計画中、承認済み、発注済み、施工中、完了、中止", Lines(aggregationRun.Output));
        Assert.Contains("  費用区分: 修繕費、設備投資", Lines(aggregationRun.Output));
        Assert.Contains("  集計基準日: 2026-10-06(当日)", Lines(aggregationRun.Output));
        Assert.Equal(ConsoleApp.Succeeded, remainingRun.ExitCode);
        AssertMatches(ExpectedTable.Load(Path.Combine(remainingCase.ExpectedDirectory, "残予算と見込み残.csv")), remaining);
    }

    [Fact]
    public void List_items_can_be_separated_by_comma()
    {
        var testCase = TestCases.Get("例12/条件2");

        var run = Run([ConsoleArguments.Input, testCase.InputDirectory, ConsoleArguments.Output, OutputDirectory, ConsoleArguments.StatusesOption, "承認済み, 発注済み,施工中,完了,中止", ConsoleArguments.CategoriesOption, "修繕費,設備投資"]);

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        AssertMatches(ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, "山積み.csv")), ReadWritten("山積み"));
        Assert.Contains("  含める状態: 承認済み、発注済み、施工中、完了、中止", Lines(run.Output));
    }

    [Fact]
    public void Existing_files_are_overwritten_and_other_files_are_kept()
    {
        var testCase = TestCases.Get("例12/条件1");
        Directory.CreateDirectory(OutputDirectory);
        var memo = Path.Combine(OutputDirectory, "メモ.txt");
        File.WriteAllText(memo, "残す");
        var otherCsv = Path.Combine(OutputDirectory, "工事.csv");
        File.Copy(Path.Combine(testCase.InputDirectory, "工事.csv"), otherCsv);

        var first = Run([.. ArgumentsOf(testCase), ConsoleArguments.AmountKindOption, "見積額"]);
        var second = Run(ArgumentsOf(testCase));

        Assert.Equal(ConsoleApp.Succeeded, first.ExitCode);
        Assert.Equal(ConsoleApp.Succeeded, second.ExitCode);
        Assert.Equal(["欄", "予算額", "見積額", "実績額", "人工"], ReadWritten("山積み").Columns);
        Assert.Equal("残す", File.ReadAllText(memo));
        Assert.Equal(File.ReadAllBytes(Path.Combine(testCase.InputDirectory, "工事.csv")), File.ReadAllBytes(otherCsv));
    }

    [Theory]
    [InlineData(new string[0], "「--input」がありません。")]
    [InlineData(new[] { "--input", "入力" }, "「--output」がありません。")]
    [InlineData(new[] { "--input" }, "「--input」の値がありません。")]
    [InlineData(new[] { "--input", "--output", "出力" }, "「--input」の値がありません。")]
    [InlineData(new[] { "--input", " ", "--output", "出力" }, "「--input」の値がありません。")]
    [InlineData(new[] { "--input", "入力", "--output", "" }, "「--output」の値がありません。")]
    [InlineData(new[] { "--input", "入力", "--output", "出力", "--status", "計画中" }, "「--status」は知らない引数です。")]
    [InlineData(new[] { "--input", "入力", "--input", "入力", "--output", "出力" }, "「--input」が2回あります。")]
    [InlineData(new[] { "--input", "入力", "--output", "出力", "--statuses", "計画中、保留" }, "「--statuses」の「保留」は計画中・承認済み・発注済み・施工中・完了・中止のどれでもありません。")]
    [InlineData(new[] { "--input", "入力", "--output", "出力", "--statuses", "計画中、、完了" }, "「--statuses」の「計画中、、完了」に空の項目があります。")]
    [InlineData(new[] { "--input", "入力", "--output", "出力", "--categories", "修繕" }, "「--categories」の「修繕」は修繕費・設備投資のどれでもありません。")]
    [InlineData(new[] { "--input", "入力", "--output", "出力", "--base-date", "2027/10/31" }, "「--base-date」の「2027/10/31」は YYYY-MM-DD の日付ではありません。")]
    [InlineData(new[] { "--input", "入力", "--output", "出力", "--amount-kind", "予算額、見積額" }, "「--amount-kind」の「予算額、見積額」は予算額・見積額・実績額のどれでもありません。")]
    public void Wrong_arguments_are_reported_with_usage(string[] args, string message)
    {
        var run = Run(args);

        Assert.Equal(ConsoleApp.UsageError, run.ExitCode);
        Assert.Equal(message, Lines(run.Error)[0]);
        Assert.EndsWith(ConsoleArguments.Usage, run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void Help_shows_usage()
    {
        var run = Run(["--help"]);

        Assert.Equal(ConsoleApp.Succeeded, run.ExitCode);
        Assert.Equal(ConsoleArguments.Usage, run.Output);
        Assert.Empty(run.Error);
    }

    [Fact]
    public void Missing_input_folder_is_reported()
    {
        var input = Path.Combine(directory, "ない");

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.UsageError, run.ExitCode);
        Assert.Equal($"入力のフォルダがありません: {input}", Lines(run.Error)[0]);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Output_must_not_be_a_file()
    {
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "出力.csv");
        File.WriteAllText(output, string.Empty);

        var run = Run([ConsoleArguments.Input, TestCases.Get("例12/条件1").InputDirectory, ConsoleArguments.Output, output]);

        Assert.Equal(ConsoleApp.UsageError, run.ExitCode);
        Assert.Equal($"出力先にはフォルダを指定します。同じ名前のファイルがあります: {output}", Lines(run.Error)[0]);
    }

    [Fact]
    public void Nothing_is_overwritten_when_an_output_file_is_in_use()
    {
        var testCase = TestCases.Get("例12/条件1");
        var first = Run([.. ArgumentsOf(testCase), ConsoleArguments.AmountKindOption, "見積額"]);

        (int ExitCode, string Output, string Error) second;
        using (new FileStream(Path.Combine(OutputDirectory, "計算に使わない修正.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            second = Run([.. ArgumentsOf(testCase), ConsoleArguments.AmountKindOption, "予算額"]);
        }

        Assert.Equal(ConsoleApp.Succeeded, first.ExitCode);
        Assert.Equal(ConsoleApp.Failed, second.ExitCode);
        Assert.Equal("書き出せません: 計算に使わない修正.csv を開けません。ほかで開かれていれば、閉じてからやり直してください。", Lines(second.Error)[0]);
        Assert.Equal(["欄", "見積額", "人工"], ReadWritten("山積み").Columns);
    }

    [Fact]
    public void Read_error_is_reported_and_nothing_is_written()
    {
        var input = CopyInput("例01-04/基本");
        ReplaceLine(Path.Combine(input, "予算年割.csv"), 3, "2,1,20x7,1731324,いいえ");

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.StartsWith("読み込めません: 予算年割.csv 3行目 列「年度」: ", Lines(run.Error)[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Calculation_error_is_reported_and_nothing_is_written()
    {
        var input = CopyInput("例01-04/基本");
        File.AppendAllText(Path.Combine(input, "予算年割.csv"), "6,1,2026,1,いいえ\n");

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.StartsWith("計算できません: ", Lines(run.Error)[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Calculation_error_in_a_later_table_keeps_previous_results()
    {
        var input = CopyInput("例01-04/基本");
        var first = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory, ConsoleArguments.AmountKindOption, "見積額"]);
        File.AppendAllText(Path.Combine(input, "予算枠.csv"), "4,2026,修繕費,1,いいえ\n");

        var second = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Succeeded, first.ExitCode);
        Assert.Equal(ConsoleApp.Failed, second.ExitCode);
        Assert.StartsWith("計算できません: ", Lines(second.Error)[0], StringComparison.Ordinal);
        Assert.Equal(["欄", "見積額", "人工"], ReadWritten("山積み").Columns);
    }

    [Fact]
    public void Duplicate_id_is_reported_as_calculation_error()
    {
        var input = CopyInput("例01-04/基本");
        File.AppendAllText(Path.Combine(input, "工事.csv"), "1,例9,ポンプ更新,施工中,2027-02-10,2027-05-24,,,,,,,,,いいえ\n");

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.StartsWith("計算できません: ", Lines(run.Error)[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    [Fact]
    public void Overflow_is_reported_as_calculation_error()
    {
        var input = CopyInput("例12/条件1");
        File.AppendAllText(Path.Combine(input, "実績.csv"), "2,4,2027-09,5000000000000000000,,いいえ\n3,4,2027-09,5000000000000000000,,いいえ\n");

        var run = Run([ConsoleArguments.Input, input, ConsoleArguments.Output, OutputDirectory]);

        Assert.Equal(ConsoleApp.Failed, run.ExitCode);
        Assert.Equal("計算できません: 金額か人工が大きすぎます。", Lines(run.Error)[0]);
        Assert.False(Directory.Exists(OutputDirectory));
    }

    private static string KindNameOf(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.EndsWith(ExpectedTable.PartialSuffix, StringComparison.Ordinal) ? stem[..^ExpectedTable.PartialSuffix.Length] : stem;
    }

    /// <summary>ケースの入力と、ケース.csv に書かれた集計の条件を渡す引数。</summary>
    private string[] ArgumentsOf(TestCase testCase)
    {
        var args = new List<string> { ConsoleArguments.Input, testCase.InputDirectory, ConsoleArguments.Output, OutputDirectory };
        foreach (var (item, option) in new[] { ("集計基準日", ConsoleArguments.BaseDateOption), ("含める状態", ConsoleArguments.StatusesOption), ("費用区分", ConsoleArguments.CategoriesOption) })
        {
            if (testCase.Properties.TryGetValue(item, out var value))
            {
                args.AddRange([option, value]);
            }
        }

        return [.. args];
    }

    private static (int ExitCode, string Output, string Error) Run(string[] args, DateOnly? today = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ConsoleApp.Run(args, output, error, today ?? Today);
        return (exitCode, output.ToString(), error.ToString());
    }

    private TextTable ReadWritten(string name)
    {
        var table = CsvTable.Read(Path.Combine(OutputDirectory, name + ".csv"));
        return new TextTable(table.Columns, [.. table.Rows.Select(row => row.Cells)]);
    }

    /// <summary>画面に出た未入力の件数(見出しの下の字下げした行)を、「金額の種類・件数・対象」の表にする。形の違う行があれば失敗にする。</summary>
    private static TextTable ShownMissingAmounts(string output)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in Lines(output).SkipWhile(line => line != "未入力の件数:").Skip(1).TakeWhile(line => line.StartsWith(' ')))
        {
            var match = MissingAmountLine().Match(line);
            Assert.True(match.Success, $"未入力の件数の行の形が違います: {line}");
            rows.Add([match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value]);
        }

        return new TextTable(["金額の種類", "件数", "対象"], rows);
    }

    /// <summary>小数点以下1桁の人工を、10倍した整数にする。</summary>
    private static long Tenths(string text) => long.Parse(text.Replace(".", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

    private static string TenthsText(long tenths) => string.Create(CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10}");

    private static void AssertMatches(ExpectedTable expected, TextTable actual)
    {
        var differences = TableComparison.Compare(expected, actual);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    /// <summary>期待値の表を、指定した列だけに絞る。</summary>
    private static ExpectedTable Narrow(ExpectedTable expected, IReadOnlyList<string> columns)
    {
        var indexes = columns.Select(column => ExpectedTable.IndexOf(expected.Columns, column)).ToList();
        var rows = expected.Table.Rows.Select(row => new CsvTableRow(row.LineNumber, [.. indexes.Select(index => row.Cells[index])])).ToList();
        return expected with { Table = new CsvTable(expected.Table.FilePath, columns, rows) };
    }

    private string CopyInput(string caseId)
    {
        var input = Path.Combine(directory, "入力");
        Directory.CreateDirectory(input);
        foreach (var file in Directory.EnumerateFiles(TestCases.Get(caseId).InputDirectory))
        {
            File.Copy(file, Path.Combine(input, Path.GetFileName(file)));
        }

        return input;
    }

    /// <summary>ファイルの lineNumber 行目(1から数える)を置き換える。</summary>
    private static void ReplaceLine(string filePath, int lineNumber, string text)
    {
        var lines = File.ReadAllLines(filePath);
        lines[lineNumber - 1] = text;
        File.WriteAllLines(filePath, lines);
    }

    private static string[] Lines(string text) => text.Split(Environment.NewLine);

    [GeneratedRegex(@"^  (\S+) ([0-9]+)件(?:\((.+)\))?\z")]
    private static partial Regex MissingAmountLine();
}
