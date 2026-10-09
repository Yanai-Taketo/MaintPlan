using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MaintPlan.IO.Results;

namespace MaintPlan.IO.Excel;

/// <summary>
/// 結果の表を、表ごとのシートを置いたブック(.xlsx)1冊に書く。シート名は表の名前、シートの順は表の順で、1行目を列名とする。
/// セルは文字のセルとし、数値のセルにする列の数だけを数値のセルにする。表示の書式は付けない(標準のまま)。
/// </summary>
public static partial class ResultWorkbookWriter
{
    /// <summary>数値のセルにする列。表の名前ごとに挙げる。</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NumericColumns = new Dictionary<string, IReadOnlyList<string>>
    {
        ["月ごとの値"] = ["予算額", "見積額", "実績額", "人工"],
        ["予算額の初期値"] = ["労務費", "予算額の初期値"],
        ["山積み"] = ["予算額", "見積額", "実績額", "人工"],
        ["人工の内訳"] = ["人工"],
        ["残予算と見込み残"] = ["予算枠", "実績額", "残予算", "未実績見込み", "見込み残"],
        ["計算に使わない修正"] = ["金額"],
    };

    /// <summary>
    /// ブックを作り直して path に書く。同じ名前のファイルは上書きし(ブックにあったほかのシートは残らない)、親のフォルダがなければ作る。
    /// 空の値のセルには、何も入れない。
    /// </summary>
    public static void Write(string path, IReadOnlyList<NamedTable> tables)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, table) in tables)
        {
            var sheet = workbook.Worksheets.Add(name);
            var numericColumns = NumericColumns.GetValueOrDefault(name, []);
            for (var column = 0; column < table.Columns.Count; column++)
            {
                sheet.Cell(1, column + 1).Value = table.Columns[column];
            }

            for (var row = 0; row < table.Rows.Count; row++)
            {
                for (var column = 0; column < table.Columns.Count; column++)
                {
                    var text = table.Rows[row][column];
                    if (text.Length > 0)
                    {
                        sheet.Cell(row + 2, column + 1).Value = numericColumns.Contains(table.Columns[column]) && NumberOf(text) is { } number ? number : text;
                    }
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        workbook.SaveAs(stream);
    }

    /// <summary>
    /// 桁区切りのない整数か小数で、有効数字(符号・小数点・先頭の0を除いた数字の数)が15桁以下なら、その数。ほかは null。
    /// 有効数字が15桁を超える数は、数値のセルにすると桁が落ちるので、文字のセルにする。
    /// </summary>
    private static decimal? NumberOf(string text) =>
        NumberPattern().IsMatch(text) && text.Where(char.IsAsciiDigit).SkipWhile(digit => digit == '0').Count() <= 15
            ? decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : null;

    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?\z")]
    private static partial Regex NumberPattern();
}
