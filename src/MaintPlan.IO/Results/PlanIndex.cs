using MaintPlan.Core.Model;

namespace MaintPlan.IO.Results;

/// <summary>計算結果の ID から、表に書く工事・費用内訳などを引くための索引。</summary>
internal sealed class PlanIndex
{
    private readonly HashSet<int> costItemsWithBudget;

    public PlanIndex(PlanData plan)
    {
        Works = plan.ConstructionWorks.ToDictionary(work => work.Id);
        CostItems = plan.CostItems.ToDictionary(item => item.Id);
        LaborLines = plan.LaborLines.ToDictionary(line => line.Id);
        StaffCategories = plan.StaffCategories.ToDictionary(category => category.Id);
        MonthlyOverrides = plan.MonthlyOverrides.ToDictionary(monthlyOverride => monthlyOverride.Id);
        costItemsWithBudget = [.. plan.AnnualBudgets.Where(annual => !annual.IsDeleted).Select(annual => annual.CostItemId)];
    }

    public IReadOnlyDictionary<int, ConstructionWork> Works { get; }

    public IReadOnlyDictionary<int, CostItem> CostItems { get; }

    public IReadOnlyDictionary<int, LaborLine> LaborLines { get; }

    public IReadOnlyDictionary<int, StaffCategory> StaffCategories { get; }

    public IReadOnlyDictionary<int, MonthlyOverride> MonthlyOverrides { get; }

    public ConstructionWork WorkOf(int costItemId) => Works[CostItems[costItemId].ConstructionWorkId];

    /// <summary>見積額が未入力(空欄)か。</summary>
    public bool IsEstimateMissing(int costItemId) => CostItems[costItemId].EstimateAmount is null;

    /// <summary>予算額が未入力(年割が1件もない)か。</summary>
    public bool IsBudgetMissing(int costItemId) => !costItemsWithBudget.Contains(costItemId);
}
