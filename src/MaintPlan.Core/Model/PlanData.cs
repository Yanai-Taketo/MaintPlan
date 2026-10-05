namespace MaintPlan.Core.Model;

/// <summary>計算に使う9つのテーブルの内容。削除済みの行も含む。</summary>
public sealed record PlanData
{
    public required IReadOnlyList<ConstructionWork> ConstructionWorks { get; init; }

    public required IReadOnlyList<CostItem> CostItems { get; init; }

    public required IReadOnlyList<AnnualBudget> AnnualBudgets { get; init; }

    public required IReadOnlyList<ActualCost> ActualCosts { get; init; }

    public required IReadOnlyList<MonthlyOverride> MonthlyOverrides { get; init; }

    public required IReadOnlyList<LaborLine> LaborLines { get; init; }

    public required IReadOnlyList<StaffCategory> StaffCategories { get; init; }

    public required IReadOnlyList<UnitRate> UnitRates { get; init; }

    public required IReadOnlyList<BudgetFrame> BudgetFrames { get; init; }
}
