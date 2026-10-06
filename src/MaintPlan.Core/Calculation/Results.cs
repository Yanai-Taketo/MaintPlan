using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>月ごとの値を入れる欄。月、年度の時期未定、年度のない時期未定のどれか。</summary>
public readonly record struct Slot
{
    private Slot(YearMonth? month, int? undeterminedFiscalYear)
    {
        Month = month;
        UndeterminedFiscalYear = undeterminedFiscalYear;
    }

    /// <summary>月。時期未定なら null。</summary>
    public YearMonth? Month { get; }

    /// <summary>時期未定の欄の年度。年度のない時期未定と、月の欄では null。</summary>
    public int? UndeterminedFiscalYear { get; }

    public bool IsUndetermined => Month is null;

    /// <summary>欄の年度。年度のない時期未定なら null。</summary>
    public int? FiscalYear => Month?.FiscalYear ?? UndeterminedFiscalYear;

    public static Slot Of(YearMonth month) => new(month, null);

    public static Slot Undetermined(int? fiscalYear) => new(null, fiscalYear);
}

/// <summary>期間のうち、ある月に入る暦日数。</summary>
public readonly record struct MonthDays(YearMonth Month, int Days);

/// <summary>年度ごとの金額。</summary>
public readonly record struct FiscalYearAmount(int FiscalYear, long Amount);

/// <summary>月への割り振りの結果。</summary>
public sealed record MonthlyAllocationResult(
    IReadOnlyList<CostItemSlotAmount> Amounts,
    IReadOnlyList<LaborLineSlotManDays> ManDays,
    IReadOnlyList<UnusedOverride> UnusedOverrides);

/// <summary>費用内訳の、ある欄・ある金額の種類の値。</summary>
public sealed record CostItemSlotAmount(int CostItemId, AmountKind Kind, Slot Slot, long Amount);

/// <summary>作業明細の、ある欄の人工(10倍した整数)。</summary>
public sealed record LaborLineSlotManDays(int LaborLineId, Slot Slot, long ManDaysTenths);

/// <summary>計算に使わず、画面で知らせる月別修正。</summary>
public sealed record UnusedOverride(int MonthlyOverrideId, UnusedOverrideReason Reason);

public enum UnusedOverrideReason
{
    /// <summary>工期に入らない月</summary>
    OutsidePeriod,
    /// <summary>完成工事高の費用内訳</summary>
    CompletionRecognition,
}

/// <summary>予算額の初期値の計算結果。</summary>
public sealed record BudgetInitialValueResult
{
    /// <summary>初期値を出したか。</summary>
    public required bool IsProduced { get; init; }

    /// <summary>労務費。初期値を出さないときは null。</summary>
    public long? LaborCost { get; init; }

    /// <summary>予算額の初期値(労務費+その他の費用)。初期値を出さないときは null。</summary>
    public long? Total { get; init; }

    /// <summary>年割の初期値。</summary>
    public IReadOnlyList<FiscalYearAmount> Allocations { get; init; } = [];

    /// <summary>作業明細の行ごと・年度ごとの労務費。</summary>
    public IReadOnlyList<LaborCostEntry> LaborCostEntries { get; init; } = [];

    /// <summary>足りない単価。</summary>
    public IReadOnlyList<MissingUnitRate> MissingUnitRates { get; init; } = [];
}

/// <summary>作業明細の行・年度ごとの労務費。人工は10倍した整数。</summary>
public sealed record LaborCostEntry(int LaborLineId, int FiscalYear, long ManDaysTenths, long UnitRate, long Cost);

/// <summary>登録されていない単価。</summary>
public sealed record MissingUnitRate(int StaffCategoryId, int FiscalYear);

/// <summary>山積みの集計の条件。</summary>
public sealed record AggregationCondition(
    AmountKind AmountKind,
    IReadOnlySet<WorkStatus> Statuses,
    IReadOnlySet<CostCategory> Categories);

/// <summary>山積みの欄。四半期、年度の時期未定、年度のない時期未定、年度の合計のどれか。既定値は年度のない時期未定。</summary>
public readonly record struct AggregationColumn
{
    private readonly bool isFiscalYearTotal;

    private AggregationColumn(int? fiscalYear, int? quarter, bool isFiscalYearTotal)
    {
        FiscalYear = fiscalYear;
        Quarter = quarter;
        this.isFiscalYearTotal = isFiscalYearTotal;
    }

    public AggregationColumnKind Kind =>
        Quarter is not null ? AggregationColumnKind.Quarter
        : isFiscalYearTotal ? AggregationColumnKind.FiscalYearTotal
        : AggregationColumnKind.Undetermined;

    /// <summary>欄の年度。年度のない時期未定なら null。</summary>
    public int? FiscalYear { get; }

    /// <summary>四半期(1〜4)。四半期の欄でなければ null。</summary>
    public int? Quarter { get; }

    public static AggregationColumn OfQuarter(int fiscalYear, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);
        return new(fiscalYear, quarter, isFiscalYearTotal: false);
    }

    public static AggregationColumn Undetermined(int? fiscalYear) => new(fiscalYear, null, isFiscalYearTotal: false);

    public static AggregationColumn FiscalYearTotal(int fiscalYear) => new(fiscalYear, null, isFiscalYearTotal: true);
}

/// <summary>山積みの欄の種類。年度の中では、この順に並べる。</summary>
public enum AggregationColumnKind
{
    /// <summary>四半期(1Q〜4Q)</summary>
    Quarter,
    /// <summary>時期未定</summary>
    Undetermined,
    /// <summary>年度の合計(1Q〜4Qと、その年度の時期未定の合計)</summary>
    FiscalYearTotal,
}

/// <summary>山積みの集計の結果。</summary>
public sealed record AggregationResult(
    IReadOnlyList<AggregationCell> Cells,
    IReadOnlyList<LaborBreakdownCell> LaborBreakdown,
    IReadOnlyList<int> MissingAmountCostItemIds);

/// <summary>山積みの欄ごとの金額と人工(10倍した整数)。</summary>
public sealed record AggregationCell(AggregationColumn Column, long Amount, long ManDaysTenths);

/// <summary>山積みの欄・人員区分ごとの人工(10倍した整数)。StaffCategoryId が null なら、種別ごとの合計。</summary>
public sealed record LaborBreakdownCell(AggregationColumn Column, StaffKind StaffKind, int? StaffCategoryId, long ManDaysTenths);

/// <summary>残予算と見込み残の計算結果。</summary>
public sealed record RemainingBudgetResult(
    IReadOnlyList<RemainingBudgetRow> Rows,
    IReadOnlyList<UnrealizedEntry> UnrealizedEntries);

/// <summary>年度×費用区分ごとの残予算と見込み残。</summary>
public sealed record RemainingBudgetRow(
    int FiscalYear,
    CostCategory Category,
    long BudgetFrame,
    bool IsBudgetFrameRegistered,
    long Actual,
    long Remaining,
    long Unrealized,
    long Projected);

/// <summary>費用内訳・年度ごとの年度配分と未実績見込み。</summary>
public sealed record UnrealizedEntry(int CostItemId, int FiscalYear, long Allocation, long Unrealized);

/// <summary>保存できるかどうかの確認の結果。</summary>
public sealed record SaveValidationResult(IReadOnlyList<SaveViolation> Violations)
{
    public bool CanSave => Violations.Count == 0;
}

/// <summary>保存できない理由。</summary>
public sealed record SaveViolation(SaveViolationKind Kind, int? CostItemId, int? LaborLineId);

public enum SaveViolationKind
{
    /// <summary>作業明細の期間が工期の外</summary>
    LaborLineOutsidePeriod,
    /// <summary>工事の日付が空欄で作業明細に日付がある</summary>
    LaborLineDatedWithoutWorkDates,
    /// <summary>見積額の修正の合計が見積額を超える</summary>
    EstimateOverridesExceed,
    /// <summary>見積額の全部の月の修正の合計が見積額と一致しない</summary>
    EstimateOverridesMismatch,
    /// <summary>予算額の修正の合計が年割額を超える</summary>
    BudgetOverridesExceed,
    /// <summary>予算額の全部の月の修正の合計が年割額と一致しない</summary>
    BudgetOverridesMismatch,
}
