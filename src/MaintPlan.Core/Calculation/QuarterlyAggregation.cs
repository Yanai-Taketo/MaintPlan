using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>山積みの集計。</summary>
public static class QuarterlyAggregation
{
    /// <summary>
    /// 集計の条件(状態・費用区分)に合う費用内訳の月ごとの値を、その月の年度と四半期の欄に合計する。時期未定の値は、その時期未定の欄に入れる。
    /// 年度の合計は、1Q〜4Qと、その年度の時期未定の欄を足した値とする。
    /// 人工は金額の種類によらず作業明細から求め、人員区分の種別ごとと区分ごとの内訳も出す。
    /// 欄は、金額か人工の値が1件でも入ったものと、その年度の合計を、欄の順に返す。
    /// 金額が未入力の費用内訳は0円として集計し、その ID を返す。
    /// </summary>
    public static AggregationResult Calculate(PlanData plan, AggregationCondition condition)
    {
        var works = plan.ConstructionWorks.ToDictionary(work => work.Id);
        var laborLines = plan.LaborLines.ToDictionary(line => line.Id);
        var categories = plan.StaffCategories.ToDictionary(category => category.Id);
        var items = CalculationTargets.CostItems(plan)
            .Where(item => condition.Statuses.Contains(works[item.ConstructionWorkId].Status) && condition.Categories.Contains(item.Category))
            .Select(item => item.Id)
            .ToHashSet();
        var allocation = MonthlyAllocation.Calculate(plan);

        var amounts = allocation.Amounts
            .Where(amount => amount.Kind == condition.AmountKind && items.Contains(amount.CostItemId))
            .SelectMany(amount => ColumnsOf(amount.Slot).Select(column => (Column: column, amount.Amount, ManDaysTenths: 0L)));
        var manDays = allocation.ManDays
            .Select(entry => (Entry: entry, Line: laborLines[entry.LaborLineId]))
            .Where(pair => items.Contains(pair.Line.CostItemId))
            .Select(pair => (pair.Entry, Category: CategoryOf(pair.Line, categories)))
            .ToList();

        var cells = amounts
            .Concat(manDays.SelectMany(pair => ColumnsOf(pair.Entry.Slot).Select(column => (Column: column, Amount: 0L, pair.Entry.ManDaysTenths))))
            .GroupBy(entry => entry.Column)
            .Select(group => new AggregationCell(group.Key, group.Sum(entry => entry.Amount), group.Sum(entry => entry.ManDaysTenths)))
            .OrderBy(cell => cell.Column)
            .ToList();
        var breakdown = manDays
            .SelectMany(pair => ColumnsOf(pair.Entry.Slot).SelectMany(column => new[]
            {
                new BreakdownEntry(column, pair.Category, IsKindTotal: true, pair.Entry.ManDaysTenths),
                new BreakdownEntry(column, pair.Category, IsKindTotal: false, pair.Entry.ManDaysTenths),
            }))
            .GroupBy(entry => (entry.Column, entry.Category.Kind, StaffCategoryId: entry.IsKindTotal ? (int?)null : entry.Category.Id))
            .OrderBy(group => (
                group.Key.Column,
                group.Key.StaffCategoryId is null ? StaffRowOrder.Of(group.Key.Kind) : StaffRowOrder.Of(group.First().Category)))
            .Select(group => new LaborBreakdownCell(group.Key.Column, group.Key.Kind, group.Key.StaffCategoryId, group.Sum(entry => entry.ManDaysTenths)))
            .ToList();
        var missing = CalculationTargets.MissingAmountCostItemIds(plan, condition.AmountKind).Where(items.Contains).Order().ToList();

        return new AggregationResult(cells, breakdown, missing);
    }

    /// <summary>月ごとの値が入る欄。月はその年度と四半期の欄、時期未定はその時期未定の欄とし、年度があればその年度の合計の欄も返す。</summary>
    private static IEnumerable<AggregationColumn> ColumnsOf(Slot slot)
    {
        var column = slot.Month is { } month
            ? AggregationColumn.OfQuarter(month.FiscalYear, month.Quarter)
            : AggregationColumn.Undetermined(slot.UndeterminedFiscalYear);
        return slot.FiscalYear is { } fiscalYear ? [column, AggregationColumn.FiscalYearTotal(fiscalYear)] : [column];
    }

    private static StaffCategory CategoryOf(LaborLine line, Dictionary<int, StaffCategory> categories) =>
        categories.TryGetValue(line.StaffCategoryId, out var category)
            ? category
            : throw new InvalidOperationException($"作業明細(ID {line.Id})の人員区分(ID {line.StaffCategoryId})がありません。");

    /// <summary>人工の内訳の項目。IsKindTotal なら種別ごとの合計に、そうでなければ区分ごとの値に入れる。</summary>
    private sealed record BreakdownEntry(AggregationColumn Column, StaffCategory Category, bool IsKindTotal, long ManDaysTenths);
}
