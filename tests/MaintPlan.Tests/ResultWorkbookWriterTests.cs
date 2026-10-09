using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MaintPlan.IO.Excel;
using MaintPlan.IO.Results;

namespace MaintPlan.Tests;

/// <summary>結果の表をブックに書き出し、シートの名前と順、列名、セルの型と書式を確かめる。</summary>
public sealed class ResultWorkbookWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    public ResultWorkbookWriterTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string WorkbookPath => Path.Combine(directory, "結果.xlsx");

    /// <summary>
    /// 確認用コンソールが書き出す6つの表を、表の順に並べたもの。列は確認用コンソールと同じ。
    /// 値は、設計書4章の表にある形(「未入力」「出さない」「0(未登録)」、小数点以下1桁の人工、負の金額など)にする。
    /// </summary>
    private static IReadOnlyList<NamedTable> SampleTables =>
    [
        new("月ごとの値", new TextTable(
            ["例", "費用内訳", "年月", "予算額", "見積額", "実績額", "人工"],
            [
                ["例3", "修繕費", "2027年度 時期未定", "665000", "未入力", "0", "15.0"],
                ["123456", "設備投資", "2027-04", "1731324", "865384", "100000", "6.9"],
            ])),
        new("予算額の初期値", new TextTable(
            ["例", "費用内訳", "労務費", "予算額の初期値", "年割の初期値", "知らせる内容"],
            [
                ["例1", "修繕費", "2834400", "3334400", "2026年度 1603076、2027年度 1731324", "なし"],
                ["例11A", "修繕費", "", "出さない", "", "足りない単価:直営・電気の2028年度"],
            ])),
        new("山積み", new TextTable(
            ["欄", "予算額", "見積額", "実績額", "人工"],
            [
                ["2027年度 1Q", "900000", "910000", "0", "12.1"],
                ["2027年度 時期未定", "未入力", "500000", "0", "4.0"],
            ])),
        new("人工の内訳", new TextTable(
            ["欄", "人員区分", "人工"],
            [
                ["2027年度 1Q", "直営・機械", "9.1"],
                ["2027年度 合計", "協力会社", "0.0"],
            ])),
        new("残予算と見込み残", new TextTable(
            ["年度・費用区分", "予算枠", "実績額", "残予算", "未実績見込み", "見込み残"],
            [
                ["2026年度 設備投資", "0(未登録)", "800000", "-800000", "0", "-800000"],
                ["2027年度 修繕費", "3000000", "750000", "2250000", "400000", "1850000"],
            ])),
        new("計算に使わない修正", new TextTable(
            ["例", "費用内訳", "金額の種類", "年月", "金額", "理由"],
            [
                ["例15", "設備投資", "見積額", "2027-06", "400000", "完成工事高の費用内訳"],
                ["例15", "修繕費", "予算額", "2027-03", "-50000", "工期に入らない月"],
            ])),
    ];

    [Fact]
    public void Sheets_are_named_by_the_tables_in_table_order()
    {
        ResultWorkbookWriter.Write(WorkbookPath, SampleTables);

        using var workbook = new XLWorkbook(WorkbookPath);
        Assert.Equal(["月ごとの値", "予算額の初期値", "山積み", "人工の内訳", "残予算と見込み残", "計算に使わない修正"], workbook.Worksheets.Select(sheet => sheet.Name));
    }

    [Fact]
    public void Sheets_have_the_columns_and_values_of_the_tables()
    {
        ResultWorkbookWriter.Write(WorkbookPath, SampleTables);

        using var workbook = new XLWorkbook(WorkbookPath);
        foreach (var table in SampleTables)
        {
            ResultSheets.AssertMatches(workbook.Worksheet(table.Name), table.Name, table.Table);
        }
    }

    /// <summary>数値のセルにする列(月ごとの値の予算額・人工など)の、桁区切りのない整数と小数は、数値のセルにする。</summary>
    [Theory]
    [InlineData("月ごとの値", "D2", "665000")]
    [InlineData("月ごとの値", "F2", "0")]
    [InlineData("月ごとの値", "G2", "15.0")]
    [InlineData("月ごとの値", "E3", "865384")]
    [InlineData("月ごとの値", "G3", "6.9")]
    [InlineData("予算額の初期値", "C2", "2834400")]
    [InlineData("予算額の初期値", "D2", "3334400")]
    [InlineData("山積み", "B2", "900000")]
    [InlineData("山積み", "D2", "0")]
    [InlineData("山積み", "E2", "12.1")]
    [InlineData("山積み", "C3", "500000")]
    [InlineData("人工の内訳", "C2", "9.1")]
    [InlineData("人工の内訳", "C3", "0.0")]
    [InlineData("残予算と見込み残", "B3", "3000000")]
    [InlineData("残予算と見込み残", "C2", "800000")]
    [InlineData("残予算と見込み残", "D2", "-800000")]
    [InlineData("残予算と見込み残", "E2", "0")]
    [InlineData("残予算と見込み残", "F3", "1850000")]
    [InlineData("計算に使わない修正", "E2", "400000")]
    [InlineData("計算に使わない修正", "E3", "-50000")]
    public void Values_in_numeric_columns_are_number_cells(string sheetName, string address, string value)
    {
        ResultWorkbookWriter.Write(WorkbookPath, SampleTables);

        using var workbook = new XLWorkbook(WorkbookPath);
        AssertNumberCell(value, workbook.Worksheet(sheetName).Cell(address));
    }

    /// <summary>列名の行、数値のセルにする列にある文字(「未入力」など)、ほかの列の値(数字だけの管理番号、年月も)は、文字のセルにする。</summary>
    [Theory]
    [InlineData("月ごとの値", "D1", "予算額")]
    [InlineData("月ごとの値", "G1", "人工")]
    [InlineData("月ごとの値", "A2", "例3")]
    [InlineData("月ごとの値", "C2", "2027年度 時期未定")]
    [InlineData("月ごとの値", "E2", "未入力")]
    [InlineData("月ごとの値", "A3", "123456")]
    [InlineData("月ごとの値", "C3", "2027-04")]
    [InlineData("予算額の初期値", "E2", "2026年度 1603076、2027年度 1731324")]
    [InlineData("予算額の初期値", "D3", "出さない")]
    [InlineData("予算額の初期値", "F3", "足りない単価:直営・電気の2028年度")]
    [InlineData("山積み", "A2", "2027年度 1Q")]
    [InlineData("山積み", "B3", "未入力")]
    [InlineData("人工の内訳", "B2", "直営・機械")]
    [InlineData("残予算と見込み残", "B2", "0(未登録)")]
    [InlineData("計算に使わない修正", "E1", "金額")]
    [InlineData("計算に使わない修正", "C2", "見積額")]
    [InlineData("計算に使わない修正", "D2", "2027-06")]
    public void Other_values_are_text_cells(string sheetName, string address, string text)
    {
        ResultWorkbookWriter.Write(WorkbookPath, SampleTables);

        using var workbook = new XLWorkbook(WorkbookPath);
        AssertTextCell(text, workbook.Worksheet(sheetName).Cell(address));
    }

    /// <summary>有効数字は、符号・小数点・先頭の0を除いた数字の数。15桁までなら数値のセルにする。</summary>
    [Theory]
    [InlineData("予算額", "123456789012345")]
    [InlineData("予算額", "-123456789012345")]
    [InlineData("人工", "12345678901234.5")]
    [InlineData("人工", "0.123456789012345")]
    public void Numbers_with_up_to_15_significant_digits_are_number_cells(string column, string value)
    {
        var table = AggregationWith(column, value);

        ResultWorkbookWriter.Write(WorkbookPath, [new NamedTable("山積み", table)]);

        using var workbook = new XLWorkbook(WorkbookPath);
        AssertNumberCell(value, workbook.Worksheet("山積み").Cell(2, ColumnNumberOf(table, column)));
    }

    /// <summary>有効数字が15桁を超える数は、数値のセルにすると桁が落ちるため、文字のセルにする。</summary>
    [Theory]
    [InlineData("予算額", "1234567890123456")]
    [InlineData("予算額", "-1234567890123456")]
    [InlineData("人工", "123456789012345.6")]
    public void Numbers_with_more_than_15_significant_digits_are_text_cells(string column, string value)
    {
        var table = AggregationWith(column, value);

        ResultWorkbookWriter.Write(WorkbookPath, [new NamedTable("山積み", table)]);

        using var workbook = new XLWorkbook(WorkbookPath);
        AssertTextCell(value, workbook.Worksheet("山積み").Cell(2, ColumnNumberOf(table, column)));
    }

    /// <summary>表示の書式を付けない。どのセルも、書式は標準(番号0)のまま。</summary>
    [Fact]
    public void Cells_have_no_number_format()
    {
        ResultWorkbookWriter.Write(WorkbookPath, SampleTables);

        using var workbook = new XLWorkbook(WorkbookPath);
        foreach (var table in SampleTables)
        {
            var sheet = workbook.Worksheet(table.Name);
            for (var row = 1; row <= table.Table.Rows.Count + 1; row++)
            {
                for (var column = 1; column <= table.Table.Columns.Count; column++)
                {
                    Assert.Equal(0, sheet.Cell(row, column).Style.NumberFormat.NumberFormatId);
                }
            }
        }
    }

    /// <summary>同じパスに書き直すと、ブックを作り直す。前のシートも、あとから足したシートも残らない。</summary>
    [Fact]
    public void Writing_again_rebuilds_the_workbook()
    {
        ResultWorkbookWriter.Write(WorkbookPath, SampleTables);
        using (var workbook = new XLWorkbook(WorkbookPath))
        {
            workbook.Worksheets.Add("メモ").Cell(1, 1).Value = "残らない";
            workbook.Save();
        }

        var aggregation = new TextTable(["欄", "見積額", "人工"], [["2028年度 合計", "0", "0.0"]]);

        ResultWorkbookWriter.Write(WorkbookPath, [new NamedTable("山積み", aggregation)]);

        using var rewritten = new XLWorkbook(WorkbookPath);
        Assert.Equal(["山積み"], rewritten.Worksheets.Select(sheet => sheet.Name));
        ResultSheets.AssertMatches(rewritten.Worksheet("山積み"), "山積み", aggregation);
    }

    /// <summary>山積みの表で、column の列だけを value にし、ほかの金額と人工を0にした1行の表。</summary>
    private static TextTable AggregationWith(string column, string value)
    {
        IReadOnlyList<string> columns = ["欄", "予算額", "見積額", "実績額", "人工"];
        return new TextTable(columns, [[.. columns.Select(name => name == "欄" ? "2027年度 合計" : name == column ? value : "0")]]);
    }

    private static int ColumnNumberOf(TextTable table, string column)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (table.Columns[index] == column)
            {
                return index + 1;
            }
        }

        throw new ArgumentException($"列「{column}」がありません。", nameof(column));
    }

    private static void AssertNumberCell(string value, IXLCell cell)
    {
        Assert.Equal(XLDataType.Number, cell.DataType);
        Assert.Equal(decimal.Parse(value, CultureInfo.InvariantCulture), (decimal)cell.GetDouble());
    }

    private static void AssertTextCell(string text, IXLCell cell)
    {
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(text, cell.GetText());
    }
}

/// <summary>結果のブックのシートを、書き出した表と比べる。セルの型は、引継ぎ資料の「Excel の読み書き(段7 sub3)」の11の決まりで決める。</summary>
internal static class ResultSheets
{
    private static readonly Regex NumberPattern = new(@"^-?[0-9]+(\.[0-9]+)?\z");

    /// <summary>数値のセルにする列。表の名前ごとに挙げる。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> NumericColumns { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        ["月ごとの値"] = ["予算額", "見積額", "実績額", "人工"],
        ["予算額の初期値"] = ["労務費", "予算額の初期値"],
        ["山積み"] = ["予算額", "見積額", "実績額", "人工"],
        ["人工の内訳"] = ["人工"],
        ["残予算と見込み残"] = ["予算枠", "実績額", "残予算", "未実績見込み", "見込み残"],
        ["計算に使わない修正"] = ["金額"],
    };

    /// <summary>
    /// 数値のセルにする値か。数値のセルにする列にあり、桁区切りのない整数か小数で、
    /// 有効数字(符号・小数点・先頭の0を除いた数字の数)が15桁以下のもの。
    /// </summary>
    public static bool IsNumber(string tableName, string column, string text) =>
        NumericColumns[tableName].Contains(column)
        && NumberPattern.IsMatch(text)
        && text.Where(char.IsAsciiDigit).SkipWhile(digit => digit == '0').Count() <= 15;

    /// <summary>
    /// シートが、表と同じ列名と値を持つことを確かめる。1行目は列名の文字のセル。値は、IsNumber なら同じ数の数値のセル、
    /// そうでなければ同じ文字の文字のセル(空の値は、空のセルか空の文字のセル)。どのセルにも表示の書式がないこと。食い違いはまとめて示す。
    /// </summary>
    public static void AssertMatches(IXLWorksheet sheet, string tableName, TextTable table)
    {
        var differences = new List<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow != table.Rows.Count + 1 || lastColumn != table.Columns.Count)
        {
            differences.Add($"シート「{sheet.Name}」の大きさ: 期待値 {table.Rows.Count + 1}行・{table.Columns.Count}列、ブック {lastRow}行・{lastColumn}列");
        }

        for (var column = 0; column < table.Columns.Count; column++)
        {
            Check(differences, sheet, 1, column, table.Columns[column], table.Columns[column], isNumber: false);
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var column = 0; column < table.Columns.Count; column++)
            {
                var text = table.Rows[row][column];
                Check(differences, sheet, row + 2, column, table.Columns[column], text, IsNumber(tableName, table.Columns[column], text));
            }
        }

        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    private static void Check(List<string> differences, IXLWorksheet sheet, int rowNumber, int columnIndex, string columnName, string text, bool isNumber)
    {
        var cell = sheet.Cell(rowNumber, columnIndex + 1);
        var place = $"シート「{sheet.Name}」 {rowNumber}行目 列「{columnName}」";
        var matches = isNumber
            ? cell.DataType == XLDataType.Number && (decimal)cell.GetDouble() == decimal.Parse(text, CultureInfo.InvariantCulture)
            : text.Length == 0
                ? cell.Value.IsBlank || (cell.Value.IsText && cell.Value.GetText().Length == 0)
                : cell.DataType == XLDataType.Text && cell.GetText() == text;
        if (!matches)
        {
            differences.Add($"{place}: 期待値は{(isNumber ? "数値" : "文字")}のセル「{text}」、ブックは{Describe(cell)}");
        }

        if (cell.Style.NumberFormat.NumberFormatId != 0)
        {
            differences.Add($"{place}: 表示の書式(番号 {cell.Style.NumberFormat.NumberFormatId}、「{cell.Style.NumberFormat.Format}」)が付いています。");
        }
    }

    private static string Describe(IXLCell cell) => cell.DataType switch
    {
        XLDataType.Blank => "空のセル",
        XLDataType.Number => $"数値のセル「{cell.GetDouble().ToString(CultureInfo.InvariantCulture)}」",
        XLDataType.Text => $"文字のセル「{cell.GetText()}」",
        _ => $"{cell.DataType}のセル",
    };
}
