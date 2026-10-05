using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>予算額の初期値。</summary>
public static class BudgetInitialValue
{
    public static BudgetInitialValueResult Calculate(PlanData plan, int costItemId) =>
        throw new NotImplementedException();
}

/// <summary>山積みの集計。</summary>
public static class QuarterlyAggregation
{
    public static AggregationResult Calculate(PlanData plan, AggregationCondition condition) =>
        throw new NotImplementedException();
}

/// <summary>残予算と見込み残。</summary>
public static class RemainingBudget
{
    public static RemainingBudgetResult Calculate(PlanData plan, DateOnly baseDate) =>
        throw new NotImplementedException();
}

/// <summary>保存できない条件の確認。</summary>
public static class SaveValidation
{
    public static SaveValidationResult Validate(PlanData plan) =>
        throw new NotImplementedException();
}
