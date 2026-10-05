using System.Globalization;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;

namespace MaintPlan.Tests.Support;

/// <summary>tests/data の下の、例ごと・ケースごとのフォルダ。</summary>
public sealed record TestCase(string Id, string DirectoryPath, IReadOnlyDictionary<string, string> Properties)
{
    public const string CaseFileName = "ケース.csv";

    public string InputDirectory => Path.Combine(DirectoryPath, "入力");

    public string ExpectedDirectory => Path.Combine(DirectoryPath, "期待値");

    public IReadOnlyList<string> ExpectedFileNames =>
        [.. Directory.EnumerateFiles(ExpectedDirectory, "*.csv").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    /// <summary>集計基準日。</summary>
    public DateOnly BaseDate => DateOnly.ParseExact(Require("集計基準日"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>山積みに含める状態。</summary>
    public IReadOnlySet<WorkStatus> Statuses => ParseList<WorkStatus>(Require("含める状態"));

    /// <summary>山積みに含める費用区分。</summary>
    public IReadOnlySet<CostCategory> Categories => ParseList<CostCategory>(Require("費用区分"));

    public override string ToString() => Id;

    private string Require(string item) =>
        Properties.TryGetValue(item, out var value) ? value : throw new InvalidOperationException($"{Id} の{CaseFileName}に「{item}」がありません。");

    private static HashSet<T> ParseList<T>(string text)
        where T : struct, Enum =>
        [.. text.Split('、').Select(label => Labels.TryParse<T>(label, out var value) ? value : throw new FormatException($"「{label}」は{string.Join("・", Labels.All<T>())}のどれでもありません。"))];
}

public static class TestCases
{
    /// <summary>ケース.csv に書ける項目と、空欄にできないか。</summary>
    public static IReadOnlyDictionary<string, bool> CaseItems { get; } = new Dictionary<string, bool>
    {
        ["出典"] = true,
        ["変更"] = false,
        ["集計基準日"] = false,
        ["含める状態"] = false,
        ["費用区分"] = false,
    };

    private static readonly Lazy<IReadOnlyList<TestCase>> Cases = new(Discover);

    public static string DataRoot => Path.Combine(AppContext.BaseDirectory, "data");

    public static IReadOnlyList<TestCase> All => Cases.Value;

    public static TestCase Get(string id) => All.Single(testCase => testCase.Id == id);

    public static TheoryData<string> Ids => [.. All.Select(testCase => testCase.Id)];

    public static TheoryData<string, string> ExpectedFiles =>
        [.. All.SelectMany(testCase => testCase.ExpectedFileNames.Select(fileName => (testCase.Id, fileName)))];

    private static List<TestCase> Discover()
    {
        var cases = new List<TestCase>();
        foreach (var exampleDirectory in Directory.EnumerateDirectories(DataRoot).Order(StringComparer.Ordinal))
        {
            foreach (var caseDirectory in Directory.EnumerateDirectories(exampleDirectory).Order(StringComparer.Ordinal))
            {
                var id = $"{Path.GetFileName(exampleDirectory)}/{Path.GetFileName(caseDirectory)}";
                cases.Add(new TestCase(id, caseDirectory, ReadProperties(Path.Combine(caseDirectory, TestCase.CaseFileName))));
            }
        }

        return cases;
    }

    private static Dictionary<string, string> ReadProperties(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        var table = CsvTable.Read(filePath);
        var item = table.IndexOf("項目");
        var value = table.IndexOf("値");
        if (item < 0 || value < 0 || table.Columns.Count != 2)
        {
            throw new CsvFormatException(filePath, 1, null, "列は「項目」と「値」の2つです。");
        }

        var properties = new Dictionary<string, string>();
        foreach (var row in table.Rows)
        {
            if (!properties.TryAdd(row.Cells[item], row.Cells[value]))
            {
                throw new CsvFormatException(filePath, row.LineNumber, "項目", $"「{row.Cells[item]}」が重複しています。");
            }
        }

        return properties;
    }
}
