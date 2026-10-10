using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;

namespace MaintPlan.IO.Results;

/// <summary>
/// 確認用コンソールの集計の条件。状態と費用区分は山積みと人工の内訳に、集計基準日は残予算と見込み残に使う。
/// AmountKind が null なら、山積みの金額の種類を3つとも集計する。
/// </summary>
public sealed record OutputCondition(
    IReadOnlySet<WorkStatus> Statuses,
    IReadOnlySet<CostCategory> Categories,
    DateOnly BaseDate,
    AmountKind? AmountKind);

/// <summary>名前の付いた表。名前は、書き出すファイルやシートの名前にする。</summary>
public sealed record NamedTable(string Name, TextTable Table);

/// <summary>確認用コンソールが書き出す表と、表の外に示す未入力の件数。未入力の件数の列は、金額の種類・件数・対象の順。</summary>
public sealed record OutputResult(IReadOnlyList<NamedTable> Tables, TextTable MissingAmounts);

/// <summary>
/// 確認用コンソールが書き出す表を作る。
/// 表は、月ごとの値、予算額の初期値、山積み、人工の内訳、残予算と見込み残、計算に使わない修正の順とする。
/// 表を工事や費用内訳で分けて示すため、「例」「費用内訳」の列を省かずに置く。
/// </summary>
public static class OutputTables
{
    private static readonly IReadOnlyList<string> MonthlyValueColumns = ["例", "費用内訳", "年月", "予算額", "見積額", "実績額", "人工"];
    private static readonly IReadOnlyList<string> BudgetInitialValueColumns = ["例", "費用内訳", "労務費", "予算額の初期値", "年割の初期値", "知らせる内容"];
    private static readonly IReadOnlyList<string> LaborBreakdownColumns = ["欄", "人員区分", "人工"];
    private static readonly IReadOnlyList<string> RemainingBudgetColumns = ["年度・費用区分", "予算枠", "実績額", "残予算", "未実績見込み", "見込み残"];
    private static readonly IReadOnlyList<string> UnusedOverrideColumns = ["例", "費用内訳", "金額の種類", "年月", "金額", "理由"];
    private static readonly IReadOnlyList<string> MissingAmountColumns = ["金額の種類", "件数", "対象"];

    public static OutputResult Build(PlanData plan, OutputCondition condition)
    {
        AmountKind[] kinds = condition.AmountKind is { } amountKind ? [amountKind] : Enum.GetValues<AmountKind>();
        var aggregations = kinds.ToDictionary(
            kind => kind,
            kind => QuarterlyAggregation.Calculate(plan, new AggregationCondition(kind, condition.Statuses, condition.Categories)));
        var allocation = MonthlyAllocation.Calculate(plan);

        IReadOnlyList<NamedTable> tables =
        [
            new("月ごとの値", ResultTables.MonthlyValues(plan, allocation, MonthlyValueColumns)),
            new("予算額の初期値", ResultTables.BudgetInitialValues(plan, BudgetInitialValue.CalculateAll(plan), BudgetInitialValueColumns)),
            new("山積み", ResultTables.Aggregation(aggregations, ["欄", .. kinds.Select(Labels.Of), "人工"])),
            new("人工の内訳", ResultTables.LaborBreakdown(plan, aggregations[kinds[0]], LaborBreakdownColumns)),
            new("残予算と見込み残", ResultTables.RemainingBudget(RemainingBudget.Calculate(plan, condition.BaseDate), RemainingBudgetColumns)),
            new("計算に使わない修正", ResultTables.UnusedOverrides(plan, allocation, UnusedOverrideColumns)),
        ];
        return new OutputResult(tables, ResultTables.MissingAmounts(plan, aggregations, MissingAmountColumns));
    }
}
