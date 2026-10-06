using MaintPlan.IO.Csv;
using MaintPlan.IO.Results;

namespace MaintPlan.Tests;

/// <summary>結果の表を CSV に書き出し、読み直すと同じ表になることを確かめる。</summary>
public sealed class ResultCsvWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    public ResultCsvWriterTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Written_file_can_be_read_back()
    {
        var table = new TextTable(
            ["例", "金額", "理由"],
            [
                ["例1", "-800000", "工期に入らない月、完成工事高の費用内訳"],
                ["a,b", "\"引用\"", "1行目\n2行目"],
                ["", " 前後の空白 ", "0(未登録)"],
            ]);
        var path = Path.Combine(directory, "表.csv");

        ResultCsvWriter.Write(path, table);

        var read = CsvTable.Read(path);
        Assert.Equal(table.Columns, read.Columns);
        Assert.Equal(table.Rows.Select(Join), read.Rows.Select(row => Join(row.Cells)));
    }

    [Fact]
    public void Written_file_starts_with_byte_order_mark()
    {
        var path = Path.Combine(directory, "表.csv");

        ResultCsvWriter.Write(path, new TextTable(["欄"], [["2027年度 1Q"]]));

        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
    }

    [Fact]
    public void Existing_file_is_overwritten()
    {
        var path = Path.Combine(directory, "表.csv");
        ResultCsvWriter.Write(path, new TextTable(["欄"], [["2027年度 1Q"], ["2027年度 2Q"], ["2027年度 3Q"]]));

        ResultCsvWriter.Write(path, new TextTable(["欄"], [["2028年度 1Q"]]));

        var read = CsvTable.Read(path);
        Assert.Equal(["2028年度 1Q"], read.Rows.Select(row => Join(row.Cells)));
    }

    private static string Join(IReadOnlyList<string> cells) => string.Join("\u001f", cells);
}
