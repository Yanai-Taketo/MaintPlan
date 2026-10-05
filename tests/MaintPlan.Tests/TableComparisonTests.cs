using System.Text;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Results;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>期待値ファイルの読み込みと、計算結果との比べ方を確かめる。</summary>
public sealed class TableComparisonTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    public TableComparisonTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Matching_table_has_no_differences()
    {
        var expected = Expected("月ごとの値.csv", "例,年月,予算額,人工", "例1,2027-02,609168,8.7", "例1,2027-03,993908,28.4");
        var actual = Actual(["例", "年月", "予算額", "人工"], ["例1", "2027-03", "993908", "28.4"], ["例1", "2027-02", "609168", "8.7"]);

        Assert.Empty(TableComparison.Compare(expected, actual));
    }

    [Fact]
    public void Different_value_is_reported_with_line_and_column()
    {
        var expected = Expected("月ごとの値.csv", "例,年月,予算額", "例1,2027-02,609168");
        var actual = Actual(["例", "年月", "予算額"], ["例1", "2027-02", "609169"]);

        var difference = Assert.Single(TableComparison.Compare(expected, actual));
        Assert.Contains("2行目", difference);
        Assert.Contains("「予算額」", difference);
    }

    [Fact]
    public void Extra_nonzero_row_fails_only_in_full_table()
    {
        string[] lines = ["欄,予算額", "2027年度 1Q,900000"];
        var actual = Actual(["欄", "予算額"], ["2027年度 1Q", "900000"], ["2027年度 2Q", "100"]);

        Assert.Single(TableComparison.Compare(Expected("山積み.csv", lines), actual));
        Assert.Empty(TableComparison.Compare(Expected("山積み_一部.csv", lines), actual));
    }

    [Fact]
    public void Extra_zero_row_and_missing_zero_row_are_allowed()
    {
        var expected = Expected("山積み.csv", "欄,予算額,人工", "2027年度 1Q,900000,12.1", "2027年度 2Q,0,0.0");
        var actual = Actual(["欄", "予算額", "人工"], ["2027年度 1Q", "900000", "12.1"], ["2027年度 3Q", "0", "0.0"]);

        Assert.Empty(TableComparison.Compare(expected, actual));
    }

    [Fact]
    public void ManDays_written_without_decimal_matches_one_decimal()
    {
        var expected = Expected("月ごとの値.csv", "費用内訳,年月,人工", "設備投資,2027-09,0", "設備投資,2027-06,6.0");
        var actual = Actual(["費用内訳", "年月", "人工"], ["設備投資", "2027-09", "0.0"], ["設備投資", "2027-06", "6.0"]);

        Assert.Empty(TableComparison.Compare(expected, actual));
    }

    [Fact]
    public void Missing_nonzero_row_is_reported()
    {
        var expected = Expected("山積み.csv", "欄,予算額", "2027年度 1Q,900000");
        var actual = Actual(["欄", "予算額"]);

        Assert.Single(TableComparison.Compare(expected, actual));
    }

    [Fact]
    public void Empty_expected_cell_is_not_compared()
    {
        var expected = Expected("残予算と見込み残_一部.csv", "年度・費用区分,予算枠,見込み残", "2026年度 修繕費,,3500000");
        var actual = Actual(["年度・費用区分", "予算枠", "見込み残"], ["2026年度 修繕費", "5000000", "3500000"]);

        Assert.Empty(TableComparison.Compare(expected, actual));
    }

    [Fact]
    public void Text_tokens_are_compared_as_written()
    {
        var expected = Expected("月ごとの値.csv", "例,年月,見積額", "例3,2027年度 時期未定,未入力");

        Assert.Empty(TableComparison.Compare(expected, Actual(["例", "年月", "見積額"], ["例3", "2027年度 時期未定", "未入力"])));
        Assert.Single(TableComparison.Compare(expected, Actual(["例", "年月", "見積額"], ["例3", "2027年度 時期未定", "0"])));
    }

    [Fact]
    public void Key_columns_come_from_the_expected_table()
    {
        var expected = Expected("月ごとの値.csv", "年月,見積額", "2027-03,1200000");
        var actual = Actual(["例", "費用内訳", "年月", "見積額"], ["例1", "修繕費", "2027-03", "1200000"]);

        Assert.Empty(TableComparison.Compare(expected, actual));
    }

    [Fact]
    public void Missing_column_in_result_is_reported()
    {
        var expected = Expected("月ごとの値.csv", "年月,見積額,人工", "2027-03,1200000,28.4");
        var actual = Actual(["年月", "見積額"], ["2027-03", "1200000"]);

        Assert.Contains("人工", Assert.Single(TableComparison.Compare(expected, actual)));
    }

    [Fact]
    public void Ignored_column_is_not_compared()
    {
        var expected = Expected("保存の確認.csv", "保存できる,理由の種類,結果", "いいえ,作業明細の期間が工期の外,保存できない。作業明細の期間が工期の外にある");
        var actual = Actual(["保存できる", "理由の種類"], ["いいえ", "作業明細の期間が工期の外"]);

        Assert.Empty(TableComparison.Compare(expected, actual));
    }

    [Theory]
    [InlineData("月ごとの値.csv", "例,年月,予算額", "例1,2027-02,\"609,168\"")]
    [InlineData("月ごとの値.csv", "例,年月,人工", "例1,2027-02,8.75")]
    [InlineData("月ごとの値.csv", "例,年月,人工", "例1,2027-02,8.")]
    [InlineData("月ごとの値.csv", "例,年月,金額", "例1,2027-02,100")]
    [InlineData("月ごとの値.csv", "例,予算額", "例1,100")]
    [InlineData("月ごとの値.csv", "例,年月,予算額", ",2027-02,100")]
    [InlineData("月ごとの値.csv", "例,年月,予算額", "例1,2027-02,100", "例1,2027-02,200")]
    [InlineData("山積み.csv", "欄,予算額", "2027年度 5Q,100")]
    [InlineData("人工の月ごとの内訳.csv", "例,人員区分,行,年月,人工", "例1,直営,1,2027-02,1.0")]
    [InlineData("知らない種類.csv", "例,年月", "例1,2027-02")]
    public void Malformed_expected_file_is_rejected(string fileName, params string[] lines)
    {
        Assert.Throws<CsvFormatException>(() => Expected(fileName, lines));
    }

    private ExpectedTable Expected(string fileName, params string[] lines)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(true));
        return ExpectedTable.Load(path);
    }

    private static TextTable Actual(string[] columns, params string[][] rows) => new(columns, rows);
}
