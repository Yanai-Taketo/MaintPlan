using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>予算額・見積額・実績額と人工の、月への割り振り。</summary>
public static class MonthlyAllocation
{
    /// <summary>
    /// 削除済みでない工事の、削除済みでない費用内訳について、金額と人工を月ごとの値に割り振る。
    /// 工事の日付が空欄なら月に割り振らず、時期未定の欄に入れる。実績額は工事の日付によらず、入力した月の欄に入れる。
    /// </summary>
    public static MonthlyAllocationResult Calculate(PlanData plan)
    {
        var works = plan.ConstructionWorks.Where(work => !work.IsDeleted).ToDictionary(work => work.Id);
        var amounts = new List<CostItemSlotAmount>();
        var manDays = new List<LaborLineSlotManDays>();
        var unusedOverrides = new List<UnusedOverride>();

        foreach (var item in plan.CostItems.Where(item => !item.IsDeleted && works.ContainsKey(item.ConstructionWorkId)))
        {
            var work = works[item.ConstructionWorkId];
            var period = WorkPeriodOf(work);
            var overrides = UsableOverrides(plan, item, period, unusedOverrides);

            amounts.AddRange(EstimateOf(item, work, period, overrides[AmountKind.Estimate])
                .Select(entry => new CostItemSlotAmount(item.Id, AmountKind.Estimate, entry.Slot, entry.Amount)));
            amounts.AddRange(BudgetOf(plan, item, period, overrides[AmountKind.Budget])
                .Select(entry => new CostItemSlotAmount(item.Id, AmountKind.Budget, entry.Slot, entry.Amount)));
            amounts.AddRange(plan.ActualCosts
                .Where(actual => !actual.IsDeleted && actual.CostItemId == item.Id)
                .Select(actual => new CostItemSlotAmount(item.Id, AmountKind.Actual, Slot.Of(actual.Month), actual.Amount)));
            manDays.AddRange(ManDaysOf(plan, item, work, period));
        }

        var summed = amounts
            .GroupBy(amount => (amount.CostItemId, amount.Kind, amount.Slot))
            .Select(group => new CostItemSlotAmount(group.Key.CostItemId, group.Key.Kind, group.Key.Slot, group.Sum(amount => amount.Amount)))
            .ToList();
        return new MonthlyAllocationResult(summed, manDays, unusedOverrides);
    }

    /// <summary>工期の月ごとの日数。工事の日付が空欄なら null。</summary>
    private static IReadOnlyList<MonthDays>? WorkPeriodOf(ConstructionWork work) =>
        DatesOf(work.StartDate, work.EndDate, $"工事(ID {work.Id})") is { } dates ? Period.DaysByMonth(dates.Start, dates.End) : null;

    private static (DateOnly Start, DateOnly End)? DatesOf(DateOnly? start, DateOnly? end, string target) => (start, end) switch
    {
        ({ } s, { } e) => (s, e),
        (null, null) => null,
        _ => throw new InvalidOperationException($"{target}の開始日と終了日は、両方入れるか両方空欄にします。"),
    };

    /// <summary>
    /// 計算に使う月別修正を、金額の種類ごとに「年月 → 金額」で返す。
    /// 完成工事高の費用内訳の修正と、工期に入らない月の修正(工事の日付が空欄なら、すべての修正)は使わず、unusedOverrides に足す。
    /// </summary>
    private static Dictionary<AmountKind, Dictionary<YearMonth, long>> UsableOverrides(
        PlanData plan, CostItem item, IReadOnlyList<MonthDays>? period, List<UnusedOverride> unusedOverrides)
    {
        var usable = new Dictionary<AmountKind, Dictionary<YearMonth, long>>
        {
            [AmountKind.Budget] = [],
            [AmountKind.Estimate] = [],
        };

        foreach (var monthlyOverride in plan.MonthlyOverrides.Where(o => !o.IsDeleted && o.CostItemId == item.Id).OrderBy(o => o.Id))
        {
            if (item.Recognition == RecognitionMethod.Completion)
            {
                unusedOverrides.Add(new UnusedOverride(monthlyOverride.Id, UnusedOverrideReason.CompletionRecognition));
            }
            else if (period is null || !period.Any(days => days.Month == monthlyOverride.Month))
            {
                unusedOverrides.Add(new UnusedOverride(monthlyOverride.Id, UnusedOverrideReason.OutsidePeriod));
            }
            else if (!usable[monthlyOverride.Kind].TryAdd(monthlyOverride.Month, monthlyOverride.Amount))
            {
                throw new InvalidOperationException(
                    $"費用内訳(ID {item.Id})の{Labels.Of(monthlyOverride.Kind)}の月別修正で、{monthlyOverride.Month}が重複しています。");
            }
        }

        return usable;
    }

    /// <summary>見積額。出来高は工期の各月に、完成工事高は工事終了日の月に全額を計上する。</summary>
    private static IEnumerable<(Slot Slot, long Amount)> EstimateOf(
        CostItem item, ConstructionWork work, IReadOnlyList<MonthDays>? period, IReadOnlyDictionary<YearMonth, long> overrides)
    {
        if (item.EstimateAmount is not { } estimate)
        {
            return [];
        }

        if (period is null)
        {
            return [(Slot.Undetermined(work.PlannedFiscalYear), estimate)];
        }

        if (item.Recognition == RecognitionMethod.Completion)
        {
            return [(Slot.Of(period[^1].Month), estimate)];
        }

        return AllocateWithOverrides(estimate, period, overrides).Select(entry => (Slot.Of(entry.Month), entry.Amount));
    }

    /// <summary>
    /// 予算額。年割の年度ごとに割り振る。
    /// 出来高は、工期のうちその年度に入る各月に割り振る。工期がその年度に1日もかからなければ、その年度の3月に全額を計上する。
    /// 完成工事高は、終了日の年度の年割額を終了日の月に、ほかの年度の年割額をその年度の3月に全額を計上する。
    /// </summary>
    private static IEnumerable<(Slot Slot, long Amount)> BudgetOf(
        PlanData plan, CostItem item, IReadOnlyList<MonthDays>? period, IReadOnlyDictionary<YearMonth, long> overrides)
    {
        foreach (var annual in plan.AnnualBudgets.Where(annual => !annual.IsDeleted && annual.CostItemId == item.Id))
        {
            if (period is null)
            {
                yield return (Slot.Undetermined(annual.FiscalYear), annual.Amount);
                continue;
            }

            if (item.Recognition == RecognitionMethod.Completion)
            {
                var endMonth = period[^1].Month;
                yield return (Slot.Of(endMonth.FiscalYear == annual.FiscalYear ? endMonth : MarchOf(annual.FiscalYear)), annual.Amount);
                continue;
            }

            var months = period.Where(days => days.Month.FiscalYear == annual.FiscalYear).ToList();
            if (months.Count == 0)
            {
                yield return (Slot.Of(MarchOf(annual.FiscalYear)), annual.Amount);
                continue;
            }

            foreach (var (month, amount) in AllocateWithOverrides(annual.Amount, months, overrides))
            {
                yield return (Slot.Of(month), amount);
            }
        }
    }

    /// <summary>人工。作業明細の行ごとに、その行の期間(日付が空欄なら工期)の各月へ割り振る。</summary>
    private static IEnumerable<LaborLineSlotManDays> ManDaysOf(PlanData plan, CostItem item, ConstructionWork work, IReadOnlyList<MonthDays>? period)
    {
        foreach (var line in plan.LaborLines.Where(line => !line.IsDeleted && line.CostItemId == item.Id))
        {
            if (period is null)
            {
                yield return new LaborLineSlotManDays(line.Id, Slot.Undetermined(work.PlannedFiscalYear), line.ManDaysTenths);
                continue;
            }

            var days = DatesOf(line.StartDate, line.EndDate, $"作業明細(ID {line.Id})") is { } dates
                ? Period.DaysByMonth(dates.Start, dates.End)
                : period;
            foreach (var (month, tenths) in Proration.ByDays(line.ManDaysTenths, SharesOf(days)))
            {
                yield return new LaborLineSlotManDays(line.Id, Slot.Of(month), tenths);
            }
        }
    }

    /// <summary>
    /// months の各月に value を割り振る。修正した月はその金額で固定し、
    /// value から修正した月の合計を引いた残りを、修正していない月に暦日数の比で割り振る。
    /// </summary>
    private static IEnumerable<(YearMonth Month, long Amount)> AllocateWithOverrides(
        long value, IReadOnlyList<MonthDays> months, IReadOnlyDictionary<YearMonth, long> overrides)
    {
        var free = months.Where(days => !overrides.ContainsKey(days.Month)).ToList();
        var remainder = value - months.Where(days => overrides.ContainsKey(days.Month)).Sum(days => overrides[days.Month]);
        var allocated = free.Count == 0
            ? new Dictionary<YearMonth, long>()
            : Proration.ByDays(remainder, SharesOf(free)).ToDictionary(entry => entry.Key, entry => entry.Value);

        return months.Select(days => (days.Month, overrides.TryGetValue(days.Month, out var fixedAmount) ? fixedAmount : allocated[days.Month]));
    }

    private static List<(YearMonth Key, long Days)> SharesOf(IEnumerable<MonthDays> days) =>
        [.. days.Select(entry => (entry.Month, (long)entry.Days))];

    /// <summary>年度の3月。</summary>
    private static YearMonth MarchOf(int fiscalYear) => new(fiscalYear + 1, 3);
}
