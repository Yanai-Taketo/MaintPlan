using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>残予算と見込み残。</summary>
public static class RemainingBudget
{
    /// <summary>
    /// 年度×費用区分ごとに、残予算(予算枠 − 実績額の合計)と見込み残(残予算 − 未実績見込み)を求める。
    /// 実績額は入力した月の年度で数え、工事の状態によらず含める。未実績見込みは、発注済み・施工中の工事の費用内訳ごとに求めて合計する。
    /// 行は、登録された予算枠のすべてと、予算枠が未登録で実績額か未実績見込みが0でない年度・費用区分に置き、年度・費用区分の順に返す。
    /// </summary>
    public static RemainingBudgetResult Calculate(PlanData plan, DateOnly baseDate)
    {
        var baseFiscalYear = YearMonth.Of(baseDate).FiscalYear;
        var works = plan.ConstructionWorks.ToDictionary(work => work.Id);
        var items = CalculationTargets.CostItems(plan).ToDictionary(item => item.Id);
        var amounts = MonthlyAllocation.Calculate(plan).Amounts;
        var estimates = amounts.Where(amount => amount.Kind == AmountKind.Estimate).ToLookup(amount => amount.CostItemId);
        var actuals = amounts.Where(amount => amount.Kind == AmountKind.Actual).ToList();
        var actualsOfItem = actuals.ToLookup(amount => amount.CostItemId);

        var entries = items.Values
            .Where(item => works[item.ConstructionWorkId].Status is WorkStatus.Ordered or WorkStatus.InProgress)
            .OrderBy(item => item.Id)
            .SelectMany(item => UnrealizedOf(item, estimates[item.Id], actualsOfItem[item.Id].Sum(actual => actual.Amount), baseFiscalYear))
            .ToList();

        var frames = BudgetFramesOf(plan);
        var actualByKey = actuals
            .GroupBy(actual => (FiscalYear: actual.Slot.FiscalYear!.Value, items[actual.CostItemId].Category))
            .ToDictionary(group => group.Key, group => group.Sum(actual => actual.Amount));
        var unrealizedByKey = entries
            .GroupBy(entry => (entry.FiscalYear, items[entry.CostItemId].Category))
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Unrealized));
        var rows = frames.Keys
            .Concat(actualByKey.Where(pair => pair.Value != 0).Select(pair => pair.Key))
            .Concat(unrealizedByKey.Where(pair => pair.Value != 0).Select(pair => pair.Key))
            .Distinct()
            .OrderBy(key => key.FiscalYear)
            .ThenBy(key => key.Category)
            .Select(key =>
            {
                var isRegistered = frames.TryGetValue(key, out var frame);
                var actual = actualByKey.GetValueOrDefault(key);
                var unrealized = unrealizedByKey.GetValueOrDefault(key);
                var remaining = checked(frame - actual);
                return new RemainingBudgetRow(key.FiscalYear, key.Category, frame, isRegistered, actual, remaining, unrealized, checked(remaining - unrealized));
            })
            .ToList();

        return new RemainingBudgetResult(rows, entries);
    }

    /// <summary>
    /// 費用内訳1件の、年度ごとの年度配分と未実績見込み。
    /// 見積額の月ごとの値を年度ごとに合計して年度配分とし(時期未定の欄は実施年度、実施年度もなければ集計基準日の年度)、
    /// 実績額の合計を古い年度の年度配分から順に差し引く。実績額の合計が0以下なら差し引かず、年度配分が0以下の年度は飛ばす。
    /// 実績額の合計が見積額以上なら、未実績見込みはすべて0とする。
    /// 集計基準日の年度より前の年度に残った額は、集計基準日の年度に移す。
    /// 行は年度配分のある年度に置き、0でない額を移したときは、移した先の年度にも置く。
    /// </summary>
    private static IEnumerable<UnrealizedEntry> UnrealizedOf(CostItem item, IEnumerable<CostItemSlotAmount> estimates, long actualTotal, int baseFiscalYear)
    {
        var allocations = estimates
            .GroupBy(estimate => estimate.Slot.FiscalYear ?? baseFiscalYear)
            .OrderBy(group => group.Key)
            .Select(group => (FiscalYear: group.Key, Allocation: group.Sum(estimate => estimate.Amount)))
            .ToList();
        var isRealized = actualTotal >= item.EstimateAmount;
        var deductible = Math.Max(actualTotal, 0);
        var unrealized = new Dictionary<int, long>();
        foreach (var (fiscalYear, allocation) in allocations)
        {
            var deducted = allocation > 0 ? Math.Min(allocation, deductible) : 0;
            deductible -= deducted;
            unrealized[fiscalYear] = isRealized ? 0 : allocation - deducted;
        }

        var carried = unrealized.Where(pair => pair.Key < baseFiscalYear).Sum(pair => pair.Value);
        foreach (var fiscalYear in unrealized.Keys.Where(fiscalYear => fiscalYear < baseFiscalYear).ToList())
        {
            unrealized[fiscalYear] = 0;
        }

        if (carried != 0)
        {
            unrealized[baseFiscalYear] = checked(unrealized.GetValueOrDefault(baseFiscalYear) + carried);
        }

        var allocationOf = allocations.ToDictionary(entry => entry.FiscalYear, entry => entry.Allocation);
        return unrealized
            .OrderBy(pair => pair.Key)
            .Select(pair => new UnrealizedEntry(item.Id, pair.Key, allocationOf.GetValueOrDefault(pair.Key), pair.Value));
    }

    /// <summary>削除済みでない予算枠を「(年度, 費用区分) → 予算枠額」で返す。同じ年度・費用区分の予算枠が重複したら例外にする。</summary>
    private static Dictionary<(int FiscalYear, CostCategory Category), long> BudgetFramesOf(PlanData plan)
    {
        var frames = new Dictionary<(int FiscalYear, CostCategory Category), long>();
        foreach (var frame in plan.BudgetFrames.Where(frame => !frame.IsDeleted))
        {
            if (!frames.TryAdd((frame.FiscalYear, frame.Category), frame.Amount))
            {
                throw new InvalidOperationException($"予算枠で、{frame.FiscalYear}年度の{Labels.Of(frame.Category)}が重複しています。");
            }
        }

        return frames;
    }
}
