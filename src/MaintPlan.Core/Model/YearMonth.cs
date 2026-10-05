using System.Globalization;

namespace MaintPlan.Core.Model;

/// <summary>西暦の年と月。年度は4月始まりで数える。</summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    public YearMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    /// <summary>その月が属する年度。1〜3月は前の年の年度になる。</summary>
    public int FiscalYear => Month >= 4 ? Year : Year - 1;

    /// <summary>その月が属する四半期(1〜4)。4〜6月が1、1〜3月が4。</summary>
    public int Quarter => (Month + 8) % 12 / 3 + 1;

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => new(Year, Month, DaysInMonth);

    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    public static YearMonth Of(DateOnly date) => new(date.Year, date.Month);

    public YearMonth AddMonths(int months)
    {
        var index = Year * 12 + (Month - 1) + months;
        return new YearMonth(index / 12, index % 12 + 1);
    }

    /// <summary>YYYY-MM の形の文字列を読む。</summary>
    public static bool TryParse(string? text, out YearMonth value)
    {
        value = default;
        if (text is null || text.Length != 7 || text[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || year < 1 || month < 1 || month > 12)
        {
            return false;
        }

        value = new YearMonth(year, month);
        return true;
    }

    public int CompareTo(YearMonth other) => (Year, Month).CompareTo((other.Year, other.Month));

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;

    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;

    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;

    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;

    /// <summary>YYYY-MM の形で返す。</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}
