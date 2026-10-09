using System.Globalization;
using System.Text.RegularExpressions;
using MaintPlan.Core.Model;

namespace MaintPlan.IO.Csv;

/// <summary>
/// 表の1行から、決まった形の値を読む。形が違う値は、ファイル・行・列を持つ CsvFormatException を errors に足し、その項目は既定値として読む。
/// </summary>
internal sealed partial class CsvRowReader(CsvTable table, CsvTableRow row, ICollection<CsvFormatException> errors)
{
    public int Int(string column) => Required(column, OptionalInt);

    public int? OptionalInt(string column) =>
        Parse<int>(column, text => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null, "0以上の整数");

    /// <summary>金額。円の整数で、桁区切りは付けない。</summary>
    public long Amount(string column) => Required(column, OptionalAmount);

    public long? OptionalAmount(string column) =>
        Parse<long>(column, text => AmountPattern().IsMatch(text) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null, "桁区切りのない整数");

    /// <summary>小数点以下1桁までの数を、10倍した整数で返す。</summary>
    public long Tenths(string column) =>
        Required(column, name => Parse<long>(name, ParseTenths, "0以上の、小数点以下1桁までの数"));

    /// <summary>年度。西暦4桁。</summary>
    public int FiscalYear(string column) => Required(column, OptionalFiscalYear);

    public int? OptionalFiscalYear(string column) =>
        Parse<int>(column, text => FiscalYearPattern().IsMatch(text) ? int.Parse(text, CultureInfo.InvariantCulture) : null, "西暦4桁の年度");

    public DateOnly? OptionalDate(string column) =>
        Parse<DateOnly>(column, text => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null, "YYYY-MM-DD の日付");

    public YearMonth Month(string column) =>
        Required(column, name => Parse<YearMonth>(name, text => YearMonth.TryParse(text, out var value) ? value : null, "YYYY-MM の年月"));

    /// <summary>真偽。「はい」か「いいえ」。</summary>
    public bool Bool(string column) =>
        Required(column, name => Parse<bool>(name, text => text switch { "はい" => true, "いいえ" => false, _ => null }, "「はい」か「いいえ」"));

    public T Choice<T>(string column)
        where T : struct, Enum =>
        Required(column, OptionalChoice<T>);

    public T? OptionalChoice<T>(string column)
        where T : struct, Enum =>
        Parse<T>(column, text => Labels.TryParse<T>(text, out var value) ? value : null, string.Join("・", Labels.All<T>()) + "のどれか");

    /// <summary>選択項目のうち、allowed の値だけを受け付ける。</summary>
    public T Choice<T>(string column, IReadOnlyList<T> allowed)
        where T : struct, Enum =>
        Required(column, name => Parse<T>(
            name,
            text => Labels.TryParse<T>(text, out var value) && allowed.Contains(value) ? value : null,
            string.Join("・", allowed.Select(Labels.Of)) + "のどれか"));

    public string Text(string column) => Required(column, OptionalText);

    public string? OptionalText(string column) => Cell(column) is { Length: > 0 } text ? text : null;

    /// <summary>形の違う値として errors に足す。</summary>
    private void Report(string column, string message) =>
        errors.Add(new CsvFormatException(table.FilePath, row.LineNumber, column, message));

    private string Cell(string column)
    {
        var index = table.IndexOf(column);
        if (index < 0)
        {
            throw new InvalidOperationException($"列「{column}」がありません。");
        }

        return row.Cells[index];
    }

    private T? Parse<T>(string column, Func<string, T?> parse, string expected)
        where T : struct
    {
        var text = Cell(column);
        if (text.Length == 0)
        {
            return null;
        }

        if (parse(text) is { } value)
        {
            return value;
        }

        Report(column, $"「{text}」は{expected}ではありません。");
        return null;
    }

    private T Required<T>(string column, Func<string, T?> read)
        where T : struct
    {
        if (Cell(column).Length == 0)
        {
            Report(column, "空欄にできません。");
            return default;
        }

        return read(column) ?? default;
    }

    private string Required(string column, Func<string, string?> read)
    {
        if (read(column) is { } text)
        {
            return text;
        }

        Report(column, "空欄にできません。");
        return string.Empty;
    }

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
