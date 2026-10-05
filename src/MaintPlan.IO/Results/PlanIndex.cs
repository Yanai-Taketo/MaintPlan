using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;

namespace MaintPlan.IO.Results;

/// <summary>計算結果の ID から、表に書く工事・費用内訳などを引くための索引。</summary>
internal sealed class PlanIndex
{
    private readonly IReadOnlySet<int> budgetMissing;
    private readonly IReadOnlySet<int> estimateMissing;

    public PlanIndex(PlanData plan)
    {
        Works = plan.ConstructionWorks.ToDictionary(work => work.Id);
        CostItems = plan.CostItems.ToDictionary(item => item.Id);
        LaborLines = plan.LaborLines.ToDictionary(line => line.Id);
        StaffCategories = plan.StaffCategories.ToDictionary(category => category.Id);
        MonthlyOverrides = plan.MonthlyOverrides.ToDictionary(monthlyOverride => monthlyOverride.Id);
        TargetCostItems = CalculationTargets.CostItems(plan);
        budgetMissing = CalculationTargets.MissingAmountCostItemIds(plan, AmountKind.Budget);
        estimateMissing = CalculationTargets.MissingAmountCostItemIds(plan, AmountKind.Estimate);
    }

    public IReadOnlyDictionary<int, ConstructionWork> Works { get; }

    public IReadOnlyDictionary<int, CostItem> CostItems { get; }

    public IReadOnlyDictionary<int, LaborLine> LaborLines { get; }

    public IReadOnlyDictionary<int, StaffCategory> StaffCategories { get; }

    public IReadOnlyDictionary<int, MonthlyOverride> MonthlyOverrides { get; }

    /// <summary>計算の対象の費用内訳。</summary>
    public IReadOnlyList<CostItem> TargetCostItems { get; }

    public ConstructionWork WorkOf(int costItemId) => Works[CostItems[costItemId].ConstructionWorkId];

    /// <summary>その金額の種類が未入力の費用内訳か。</summary>
    public bool IsMissing(int costItemId, AmountKind kind) => kind switch
    {
        AmountKind.Budget => budgetMissing.Contains(costItemId),
        AmountKind.Estimate => estimateMissing.Contains(costItemId),
        _ => false,
    };
}
