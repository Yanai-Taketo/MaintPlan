using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>計算の対象。削除済みの印がある行と、削除済みの工事・費用内訳に属する行は対象にしない。</summary>
public static class CalculationTargets
{
    /// <summary>対象の費用内訳。削除済みでない工事に属する、削除済みでない費用内訳。</summary>
    public static IReadOnlyList<CostItem> CostItems(PlanData plan)
    {
        var works = plan.ConstructionWorks.Where(work => !work.IsDeleted).Select(work => work.Id).ToHashSet();
        return [.. plan.CostItems.Where(item => !item.IsDeleted && works.Contains(item.ConstructionWorkId))];
    }

    /// <summary>
    /// 金額が未入力の、対象の費用内訳の ID。
    /// 予算額は年割が1件もないもの、見積額は見積額が空欄のもの。実績額は未入力にならない。
    /// </summary>
    public static IReadOnlySet<int> MissingAmountCostItemIds(PlanData plan, AmountKind kind)
    {
        var items = CostItems(plan);
        return kind switch
        {
            AmountKind.Budget => items
                .Select(item => item.Id)
                .Except(plan.AnnualBudgets.Where(annual => !annual.IsDeleted).Select(annual => annual.CostItemId))
                .ToHashSet(),
            AmountKind.Estimate => items.Where(item => item.EstimateAmount is null).Select(item => item.Id).ToHashSet(),
            _ => new HashSet<int>(),
        };
    }
}
