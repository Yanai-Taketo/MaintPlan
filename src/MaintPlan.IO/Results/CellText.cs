using System.Globalization;
using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;

namespace MaintPlan.IO.Results;

/// <summary>表のセルに書く文字。</summary>
internal static class CellText
{
    /// <summary>金額が未入力の費用内訳に書く文字。</summary>
    public const string Missing = "未入力";

    /// <summary>合計の行に書く文字。</summary>
    public const string Total = "合計";

    /// <summary>予算額の初期値を出さない費用内訳に書く文字。</summary>
    public const string NotProduced = "出さない";

    /// <summary>知らせる内容がないときに書く文字。</summary>
    public const string None = "なし";

    /// <summary>予算枠が登録されていない年度・費用区分の予算枠に書く文字。</summary>
    public const string Unregistered = "0(未登録)";

    /// <summary>金額や日数。桁区切りなしの整数。</summary>
    public static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>人工。10倍した整数を、小数点以下1桁で書く。</summary>
    public static string ManDays(long tenths)
    {
        var sign = tenths < 0 ? "-" : string.Empty;
        var magnitude = Math.Abs(tenths);
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude / 10}.{magnitude % 10}");
    }

    /// <summary>年月の欄。YYYY-MM、「YYYY年度 時期未定」、「年度のない時期未定」のどれか。</summary>
    public static string Slot(Slot slot) => slot switch
    {
        { Month: { } month } => month.ToString(),
        { UndeterminedFiscalYear: { } fiscalYear } => $"{FiscalYear(fiscalYear)} 時期未定",
        _ => "年度のない時期未定",
    };

    /// <summary>年月の欄を並べる順。年度ごとに、月の欄、その年度の時期未定の欄の順とし、年度のない時期未定の欄を最後に置く。</summary>
    public static IComparable SlotOrder(Slot slot) =>
        (slot.FiscalYear ?? int.MaxValue, slot.IsUndetermined, slot.Month is { } month ? (month.Month + 8) % 12 : 0);

    /// <summary>山積みの欄。「YYYY年度 1Q」〜「YYYY年度 4Q」、「YYYY年度 時期未定」、「年度のない時期未定」、「YYYY年度 合計」のどれか。</summary>
    public static string AggregationColumn(AggregationColumn column) => column switch
    {
        { Kind: AggregationColumnKind.Quarter, FiscalYear: { } fiscalYear, Quarter: { } quarter } => string.Create(CultureInfo.InvariantCulture, $"{FiscalYear(fiscalYear)} {quarter}Q"),
        { Kind: AggregationColumnKind.Undetermined, FiscalYear: { } fiscalYear } => $"{FiscalYear(fiscalYear)} 時期未定",
        { Kind: AggregationColumnKind.Undetermined } => "年度のない時期未定",
        { Kind: AggregationColumnKind.FiscalYearTotal, FiscalYear: { } fiscalYear } => $"{FiscalYear(fiscalYear)} {Total}",
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "山積みの欄ではありません。"),
    };

    /// <summary>「2026年度 修繕費」の形の年度と費用区分。</summary>
    public static string FiscalYearCategory(int fiscalYear, CostCategory category) => $"{FiscalYear(fiscalYear)} {Labels.Of(category)}";

    /// <summary>「YYYY年度」。</summary>
    public static string FiscalYear(int fiscalYear) => string.Create(CultureInfo.InvariantCulture, $"{fiscalYear}年度");

    /// <summary>人員区分の種別ごとの合計の行に書く文字(「直営」など)。</summary>
    public static string StaffKind(StaffCategory category) => StaffKind(category.Kind);

    /// <summary>人員区分の種別ごとの合計の行に書く文字(「直営」など)。</summary>
    public static string StaffKind(StaffKind kind) => Labels.Of(kind);

    /// <summary>人員区分の区分ごとの行に書く文字(「直営・機械」など)。</summary>
    public static string StaffCategory(StaffCategory category) => $"{Labels.Of(category.Kind)}・{category.Name}";
}
