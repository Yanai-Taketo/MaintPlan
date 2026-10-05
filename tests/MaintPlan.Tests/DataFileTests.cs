using MaintPlan.IO.Csv;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>tests/data のフォルダとファイルの形を確かめる。</summary>
public class DataFileTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void Every_csv_file_is_utf8_with_bom()
    {
        var withoutBom = Directory.EnumerateFiles(TestCases.DataRoot, "*.csv", SearchOption.AllDirectories)
            .Where(path => !File.ReadAllBytes(path).AsSpan().StartsWith(Utf8Bom))
            .Select(path => Path.GetRelativePath(TestCases.DataRoot, path))
            .ToList();

        Assert.True(withoutBom.Count == 0, "BOM がないファイル: " + string.Join("、", withoutBom));
    }

    [Fact]
    public void Every_case_folder_has_case_file_input_and_expected()
    {
        Assert.NotEmpty(TestCases.All);
        var problems = new List<string>();
        foreach (var testCase in TestCases.All)
        {
            var entries = Directory.EnumerateFileSystemEntries(testCase.DirectoryPath).Select(Path.GetFileName).Order(StringComparer.Ordinal);
            string?[] expected = ["ケース.csv", "入力", "期待値"];
            if (!entries.SequenceEqual(expected.Order(StringComparer.Ordinal)))
            {
                problems.Add($"{testCase.Id}: 置くのは {string.Join("、", expected)} だけです(今あるもの: {string.Join("、", entries)})。");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [MemberData(nameof(TestCases.Ids), MemberType = typeof(TestCases))]
    public void Case_file_has_known_items(string caseId)
    {
        var testCase = TestCases.Get(caseId);
        var unknown = testCase.Properties.Keys.Where(item => !TestCases.CaseItems.ContainsKey(item)).ToList();
        Assert.True(unknown.Count == 0, $"知らない項目: {string.Join("、", unknown)}");

        foreach (var (item, required) in TestCases.CaseItems)
        {
            if (required)
            {
                Assert.False(string.IsNullOrEmpty(testCase.Properties.GetValueOrDefault(item)), $"「{item}」は空欄にできません。");
            }
        }

        if (testCase.Properties.ContainsKey("集計基準日"))
        {
            _ = testCase.BaseDate;
        }

        if (testCase.Properties.ContainsKey("含める状態"))
        {
            Assert.NotEmpty(testCase.Statuses);
        }

        if (testCase.Properties.ContainsKey("費用区分"))
        {
            Assert.NotEmpty(testCase.Categories);
        }
    }

    [Theory]
    [MemberData(nameof(TestCases.Ids), MemberType = typeof(TestCases))]
    public void Input_folder_has_one_file_per_table(string caseId)
    {
        var testCase = TestCases.Get(caseId);
        var files = Directory.EnumerateFileSystemEntries(testCase.InputDirectory).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToList();
        var tables = PlanCsvReader.TableColumns.Keys.Select(table => table + ".csv").Order(StringComparer.Ordinal).ToList();

        Assert.Equal(tables, files);
    }
}
