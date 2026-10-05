using System.Text;
using MaintPlan.IO.Csv;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>入力の CSV の読み込みで、形の違う値をファイル・行・列とともに知らせることを確かめる。</summary>
public sealed class CsvReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    public CsvReaderTests()
    {
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(TestCases.Get("例01-04/基本").InputDirectory))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Sample_input_can_be_read()
    {
        var plan = PlanCsvReader.ReadFolder(directory);

        Assert.Equal(4, plan.ConstructionWorks.Count);
        Assert.Equal(105, plan.LaborLines[0].WorkDaysTenths);
        Assert.Equal(new DateOnly(2027, 3, 1), plan.LaborLines[0].StartDate);
        Assert.Null(plan.CostItems[2].EstimateAmount);
    }

    [Theory]
    [InlineData("予算年割", "予算額", "\"1603076\n\"")]
    [InlineData("予算年割", "年度", "\"2026\n\"")]
    [InlineData("作業明細", "作業日数", "\"10.5\n\"")]
    [InlineData("予算年割", "予算額", "\"1,603,076\"")]
    [InlineData("予算年割", "予算額", "99999999999999999999")]
    [InlineData("作業明細", "作業日数", "922337203685477581.0")]
    [InlineData("作業明細", "作業日数", "10.25")]
    [InlineData("作業明細", "開始日", "2027/03/01")]
    [InlineData("実績", "年月", "2027-13")]
    [InlineData("工事", "状態", "施工ちゅう")]
    [InlineData("工事", "削除済み", "")]
    [InlineData("月別修正", "金額の種類", "実績額")]
    public void Malformed_value_is_reported_with_file_line_and_column(string table, string column, string value)
    {
        ReplaceFirstRowCell(table, column, value);

        var exception = Assert.Throws<CsvFormatException>(() => PlanCsvReader.ReadFolder(directory));

        Assert.Equal(table + ".csv", Path.GetFileName(exception.FilePath));
        Assert.Equal(2, exception.LineNumber);
        Assert.Equal(column, exception.Column);
    }

    [Fact]
    public void Line_number_points_to_the_start_of_a_record_with_line_breaks()
    {
        ReplaceFirstRowCell("工事", "備考", "\"一行目\n二行目\n三行目\"");
        var path = Path.Combine(directory, "工事.csv");
        var lines = File.ReadAllText(path).Split('\n').ToList();
        var header = lines[0].TrimStart('﻿').Split(',');
        var secondRecordStart = 5;
        var cells = lines[secondRecordStart - 1].Split(',');
        cells[Array.IndexOf(header, "状態")] = "施工ちゅう";
        lines[secondRecordStart - 1] = string.Join(',', cells);
        File.WriteAllText(path, string.Join('\n', lines), new UTF8Encoding(true));

        var exception = Assert.Throws<CsvFormatException>(() => PlanCsvReader.ReadFolder(directory));

        Assert.Equal(secondRecordStart, exception.LineNumber);
        Assert.Equal("状態", exception.Column);
    }

    [Fact]
    public void Missing_and_unknown_columns_are_reported()
    {
        var path = Path.Combine(directory, "単価.csv");
        File.WriteAllText(path, "ID,人員区分ID,年度,単価円,削除済み\n", new UTF8Encoding(true));

        var exception = Assert.Throws<CsvFormatException>(() => PlanCsvReader.ReadFolder(directory));

        Assert.Contains("足りない列: 単価", exception.Message);
        Assert.Contains("知らない列: 単価円", exception.Message);
    }

    private void ReplaceFirstRowCell(string table, string column, string value)
    {
        var path = Path.Combine(directory, table + ".csv");
        var lines = File.ReadAllText(path).Split('\n').ToList();
        var header = lines[0].TrimStart('﻿').Split(',');
        var cells = lines[1].Split(',');
        cells[Array.IndexOf(header, column)] = value;
        lines[1] = string.Join(',', cells);
        File.WriteAllText(path, string.Join('\n', lines), new UTF8Encoding(true));
    }
}
