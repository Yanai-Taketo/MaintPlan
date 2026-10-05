using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;
using MaintPlan.IO.Results;

namespace MaintPlan.Tests.Support;

/// <summary>期待値の種類に応じて計算し、期待値と同じ形の表にする。</summary>
public static class ActualTables
{
    public static TextTable Build(ExpectedKind kind, TestCase testCase, PlanData plan, IReadOnlyList<string> columns) => kind.Name switch
    {
        "月ごとの日数" => ResultTables.MonthDays(DaysOfSingleWork(plan), columns),
        "月ごとの値" => ResultTables.MonthlyValues(plan, MonthlyAllocation.Calculate(plan), columns),
        "人工の月ごとの内訳" => ResultTables.MonthlyManDays(plan, MonthlyAllocation.Calculate(plan), columns),
        "計算に使わない修正" => ResultTables.UnusedOverrides(plan, MonthlyAllocation.Calculate(plan), columns),
        "予算額の初期値" => ResultTables.BudgetInitialValues(plan, InitialValues(plan), columns),
        "労務費の内訳" => ResultTables.LaborCosts(plan, InitialValues(plan), columns),
        "山積み" => ResultTables.Aggregation(Aggregate(testCase, plan, AmountKind.Budget, AmountKind.Estimate, AmountKind.Actual), columns),
        "人工の内訳" => ResultTables.LaborBreakdown(plan, Aggregate(testCase, plan, AmountKind.Budget)[AmountKind.Budget], columns),
        "未入力の件数" => ResultTables.MissingAmounts(plan, Aggregate(testCase, plan, AmountKind.Budget, AmountKind.Estimate), columns),
        "残予算と見込み残" => ResultTables.RemainingBudget(RemainingBudget.Calculate(plan, testCase.BaseDate), columns),
        "未実績見込みの内訳" => ResultTables.UnrealizedBreakdown(plan, RemainingBudget.Calculate(plan, testCase.BaseDate), columns),
        "保存の確認" => ResultTables.SaveValidation(SaveValidation.Validate(plan), columns),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind.Name, "計算の対応がない期待値の種類です。"),
    };

    private static IReadOnlyList<MonthDays> DaysOfSingleWork(PlanData plan)
    {
        var work = plan.ConstructionWorks.Single(work => !work.IsDeleted);
        return Period.DaysByMonth(
            work.StartDate ?? throw new InvalidOperationException("工事の開始日が空欄です。"),
            work.EndDate ?? throw new InvalidOperationException("工事の終了日が空欄です。"));
    }

    private static Dictionary<int, BudgetInitialValueResult> InitialValues(PlanData plan)
    {
        var deletedWorks = plan.ConstructionWorks.Where(work => work.IsDeleted).Select(work => work.Id).ToHashSet();
        return plan.CostItems
            .Where(item => !item.IsDeleted && !deletedWorks.Contains(item.ConstructionWorkId))
            .ToDictionary(item => item.Id, item => BudgetInitialValue.Calculate(plan, item.Id));
    }

    private static Dictionary<AmountKind, AggregationResult> Aggregate(TestCase testCase, PlanData plan, params AmountKind[] kinds) =>
        kinds.ToDictionary(
            kind => kind,
            kind => QuarterlyAggregation.Calculate(plan, new AggregationCondition(kind, testCase.Statuses, testCase.Categories)));
}
