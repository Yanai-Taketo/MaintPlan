using System.Globalization;
using System.Text.RegularExpressions;
using MaintPlan.Core.Model;

namespace MaintPlan.IO.Input;

/// <summary>
/// 表の1行から、決まった形の値を読む。形が違う値は、表・行・列を持つ InputFormatException を errors に足し、その項目は既定値として読む。
/// 値は CSV と同じ文字にしてから、CSV と同じ決まりで確かめる。文字のセル(CSV のセルはすべて文字)は、その文字のまま確かめる。
/// ブックの文字でないセルは、列が受け付けるもの(AcceptedCells)だけを CSV と同じ形の文字に直し、ほかは形の誤りとする。
/// 誤りの文の値は、セルの表示される文字で示す。
/// </summary>
internal sealed partial class RowReader(InputTable table, InputRow row, ICollection<InputFormatException> errors)
{
    /// <summary>数式のセルの誤りの文。</summary>
    public const string FormulaMessage = "数式のセルは読みません。値で貼り付けてください。";

    public int Int(string column) => Required(column, OptionalInt);

    public int? OptionalInt(string column) =>
        Parse<int>(column, AcceptedCells.Integer, text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null, "0以上の整数");

    /// <summary>金額。円の整数で、桁区切りは付けない。</summary>
    public long Amount(string column) => Required(column, OptionalAmount);

    public long? OptionalAmount(string column) =>
        Parse<long>(column, AcceptedCells.Integer, text => AmountPattern().IsMatch(text) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null, "桁区切りのない整数");

    /// <summary>小数点以下1桁までの数を、10倍した整数で返す。</summary>
    public long Tenths(string column) =>
        Required(column, name => Parse<long>(name, AcceptedCells.Tenths, ParseTenths, "0以上の、小数点以下1桁までの数"));

    /// <summary>年度。西暦4桁。</summary>
    public int FiscalYear(string column) => Required(column, OptionalFiscalYear);

    public int? OptionalFiscalYear(string column) =>
        Parse<int>(column, AcceptedCells.Integer, text => FiscalYearPattern().IsMatch(text) ? int.Parse(text, CultureInfo.InvariantCulture) : null, "西暦4桁の年度");

    /// <summary>日付。YYYY-MM-DD。誤りの文は「「X」は YYYY-MM-DD の日付ではありません。」の形で、「は」の後に半角の空白を置く。</summary>
    public DateOnly? OptionalDate(string column) =>
        Parse<DateOnly>(column, AcceptedCells.Date, text => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null, " YYYY-MM-DD の日付");

    /// <summary>年月。YYYY-MM。誤りの文は「「X」は YYYY-MM の年月ではありません。」の形で、「は」の後に半角の空白を置く。</summary>
    public YearMonth Month(string column) =>
        Required(column, name => Parse<YearMonth>(name, AcceptedCells.YearMonth, text => YearMonth.TryParse(text, out var value) ? value : null, " YYYY-MM の年月"));

    /// <summary>真偽。「はい」か「いいえ」。</summary>
    public bool Bool(string column) =>
        Required(column, name => Parse<bool>(name, AcceptedCells.TextOnly, text => text switch { "はい" => true, "いいえ" => false, _ => null }, "「はい」か「いいえ」"));

    public T Choice<T>(string column)
        where T : struct, Enum =>
        Required(column, OptionalChoice<T>);

    public T? OptionalChoice<T>(string column)
        where T : struct, Enum =>
        Parse<T>(column, AcceptedCells.TextOnly, text => Labels.TryParse<T>(text, out var value) ? value : null, string.Join("・", Labels.All<T>()) + "のどれか");

    /// <summary>選択項目のうち、allowed の値だけを受け付ける。</summary>
    public T Choice<T>(string column, IReadOnlyList<T> allowed)
        where T : struct, Enum =>
        Required(column, name => Parse<T>(
            name,
            AcceptedCells.TextOnly,
            text => Labels.TryParse<T>(text, out var value) && allowed.Contains(value) ? value : null,
            string.Join("・", allowed.Select(Labels.Of)) + "のどれか"));

    public string Text(string column) => Required(column, OptionalText);

    /// <summary>文字。ブックの整数の数値のセルは、その数字の文字として読む(123456 は「123456」)。</summary>
    public string? OptionalText(string column) => Accepted(column, AcceptedCells.Integer, "文字か整数");

    /// <summary>形の違う値として errors に足す。</summary>
    private void Report(string column, string message) =>
        errors.Add(table.Error(row.LineNumber, column, message));

    private InputCell Cell(string column)
    {
        var index = table.IndexOf(column);
        if (index < 0)
        {
            throw new InvalidOperationException($"列「{column}」がありません。");
        }

        return row.Cells[index];
    }

    /// <summary>
    /// セルを、CSV と同じ形の文字にして返す。空欄なら null。列が受け付けないセルなら、形の誤りを足して null を返す。
    /// 数式のセルは、どの列でも受け付けない。数を受け付ける列の、15桁に丸めた絶対値が10の15乗以上の数値のセルは、16桁以上の数として示す。
    /// </summary>
    private string? Accepted(string column, AcceptedCells accepted, string expected)
    {
        var cell = Cell(column);
        if (cell.IsBlank)
        {
            return null;
        }

        if (cell.TextFor(accepted) is { } text)
        {
            return text;
        }

        Report(column, cell.Type switch
        {
            InputCellType.Formula => FormulaMessage,
            InputCellType.Number when cell.Number.IsTooLarge && accepted is (AcceptedCells.Integer or AcceptedCells.Tenths)
                => "16桁以上の数は、文字のセルで入れてください。",
            _ => NotExpected(cell, expected),
        });
        return null;
    }

    private T? Parse<T>(string column, AcceptedCells accepted, Func<string, T?> parse, string expected)
        where T : struct
    {
        if (Accepted(column, accepted, expected) is not { } text)
        {
            return null;
        }

        if (parse(text) is { } value)
        {
            return value;
        }

        Report(column, NotExpected(Cell(column), expected));
        return null;
    }

    private T Required<T>(string column, Func<string, T?> read)
        where T : struct
    {
        if (Cell(column).IsBlank)
        {
            Report(column, "空欄にできません。");
            return default;
        }

        return read(column) ?? default;
    }

    private string Required(string column, Func<string, string?> read)
    {
        if (Cell(column).IsBlank)
        {
            Report(column, "空欄にできません。");
            return string.Empty;
        }

        return read(column) ?? string.Empty;
    }

    private static string NotExpected(InputCell cell, string expected) => $"「{cell.Shown}」は{expected}ではありません。";

    private static long? ParseTenths(string text)
    {
        var match = TenthsPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        if (!long.TryParse(match.Groups["whole"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var whole) || whole > (long.MaxValue - 9) / 10)
        {
            return null;
        }

        var tenth = match.Groups["tenth"].Success ? match.Groups["tenth"].Value[0] - '0' : 0;
        return whole * 10 + tenth;
    }

    [GeneratedRegex(@"^-?[0-9]+\z")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^[0-9]{4}\z")]
    private static partial Regex FiscalYearPattern();

    [GeneratedRegex(@"^(?<whole>[0-9]+)(\.(?<tenth>[0-9]))?\z")]
    private static partial Regex TenthsPattern();
}
