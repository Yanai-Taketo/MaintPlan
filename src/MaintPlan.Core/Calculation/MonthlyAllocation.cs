using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>予算額・見積額・実績額と人工の、月への割り振り。</summary>
public static class MonthlyAllocation
{
    /// <summary>
    /// 対象の費用内訳について、金額と人工を月ごとの値に割り振る。
    /// 工事の日付が空欄なら月に割り振らず、時期未定の欄に入れる。実績額は工事の日付によらず、入力した月の欄に入れる。
    /// </summary>
    public static MonthlyAllocationResult Calculate(PlanData plan)
    {
        var works = plan.ConstructionWorks.ToDictionary(work => work.Id);
        var annualBudgets = plan.AnnualBudgets.Where(annual => !annual.IsDeleted).ToLookup(annual => annual.CostItemId);
        var actualCosts = plan.ActualCosts.Where(actual => !actual.IsDeleted).ToLookup(actual => actual.CostItemId);
        var monthlyOverrides = plan.MonthlyOverrides.Where(o => !o.IsDeleted).ToLookup(o => o.CostItemId);
        var laborLines = plan.LaborLines.Where(line => !line.IsDeleted).ToLookup(line => line.CostItemId);
        var amounts = new List<CostItemSlotAmount>();
        var manDays = new List<LaborLineSlotManDays>();
        var unusedOverrides = new List<UnusedOverride>();

        foreach (var item in CalculationTargets.CostItems(plan))
        {
            var work = works[item.ConstructionWorkId];
            var period = WorkPeriodOf(work);
            var (overrides, unused) = SplitOverrides(item, period, monthlyOverrides[item.Id]);
            unusedOverrides.AddRange(unused);

            amounts.AddRange(EstimateOf(item, work, period, overrides[AmountKind.Estimate])
                .Select(entry => new CostItemSlotAmount(item.Id, AmountKind.Estimate, entry.Slot, entry.Amount)));
            amounts.AddRange(BudgetOf(item, period, annualBudgets[item.Id], overrides[AmountKind.Budget])
                .Select(entry => new CostItemSlotAmount(item.Id, AmountKind.Budget, entry.Slot, entry.Amount)));
            amounts.AddRange(actualCosts[item.Id]
                .Select(actual => new CostItemSlotAmount(item.Id, AmountKind.Actual, Slot.Of(actual.Month), actual.Amount)));
            manDays.AddRange(ManDaysOf(work, period, laborLines[item.Id]));
        }

        var summed = amounts
            .GroupBy(amount => (amount.CostItemId, amount.Kind, amount.Slot))
            .Select(group => new CostItemSlotAmount(group.Key.CostItemId, group.Key.Kind, group.Key.Slot, group.Sum(amount => amount.Amount)))
            .ToList();
        return new MonthlyAllocationResult(summed, manDays, unusedOverrides);
    }

    /// <summary>工期の月ごとの日数。工事の日付が空欄なら null。</summary>
    internal static IReadOnlyList<MonthDays>? WorkPeriodOf(ConstructionWork work) =>
        DatesOf(work.StartDate, work.EndDate, $"工事(ID {work.Id})") is { } dates ? Period.DaysByMonth(dates.Start, dates.End) : null;

    /// <summary>開始日と終了日。両方空欄なら null。片方だけのときと、終了日が開始日より前のときは例外にする。</summary>
    internal static (DateOnly Start, DateOnly End)? DatesOf(DateOnly? start, DateOnly? end, string target) => (start, end) switch
    {
        ({ } s, { } e) when e < s => throw new InvalidOperationException($"{target}の終了日が開始日より前です。"),
        ({ } s, { } e) => (s, e),
        (null, null) => null,
        _ => throw new InvalidOperationException($"{target}の開始日と終了日は、両方入れるか両方空欄にします。"),
    };

    /// <summary>
    /// 月別修正を、計算に使うものと使わないものに分ける。計算に使うものは、金額の種類ごとに「年月 → 金額」で返す。
    /// 完成工事高の費用内訳の修正と、工期に入らない月の修正(工事の日付が空欄なら、すべての修正)は計算に使わない。
    /// </summary>
    internal static (Dictionary<AmountKind, Dictionary<YearMonth, long>> Usable, List<UnusedOverride> Unused) SplitOverrides(
        CostItem item, IReadOnlyList<MonthDays>? period, IEnumerable<MonthlyOverride> monthlyOverrides)
    {
        var usable = new Dictionary<AmountKind, Dictionary<YearMonth, long>>
        {
            [AmountKind.Budget] = [],
            [AmountKind.Estimate] = [],
        };
        var unused = new List<UnusedOverride>();
        var seen = new HashSet<(AmountKind, YearMonth)>();

        foreach (var monthlyOverride in monthlyOverrides.OrderBy(o => o.Id))
        {
            if (!usable.ContainsKey(monthlyOverride.Kind))
            {
                throw new InvalidOperationException($"月別修正(ID {monthlyOverride.Id})の金額の種類は、予算額か見積額です。");
            }

            if (!seen.Add((monthlyOverride.Kind, monthlyOverride.Month)))
            {
                throw new InvalidOperationException(
                    $"費用内訳(ID {item.Id})の{Labels.Of(monthlyOverride.Kind)}の月別修正で、{monthlyOverride.Month}が重複しています。");
            }

            if (item.Recognition == RecognitionMethod.Completion)
            {
                unused.Add(new UnusedOverride(monthlyOverride.Id, UnusedOverrideReason.CompletionRecognition));
            }
            else if (period is null || !period.Any(days => days.Month == monthlyOverride.Month))
            {
                unused.Add(new UnusedOverride(monthlyOverride.Id, UnusedOverrideReason.OutsidePeriod));
            }
            else
            {
                usable[monthlyOverride.Kind].Add(monthlyOverride.Month, monthlyOverride.Amount);
            }
        }

        return (usable, unused);
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
    private static List<(Slot Slot, long Amount)> BudgetOf(
        CostItem item, IReadOnlyList<MonthDays>? period, IEnumerable<AnnualBudget> annualBudgets, IReadOnlyDictionary<YearMonth, long> overrides)
    {
        var budgets = new List<(Slot Slot, long Amount)>();
        var fiscalYears = new HashSet<int>();
        foreach (var annual in annualBudgets)
        {
            if (!fiscalYears.Add(annual.FiscalYear))
            {
                throw new InvalidOperationException($"費用内訳(ID {item.Id})の年割で、{annual.FiscalYear}年度が重複しています。");
            }

            if (period is null)
            {
                budgets.Add((Slot.Undetermined(annual.FiscalYear), annual.Amount));
                continue;
            }

            if (item.Recognition == RecognitionMethod.Completion)
            {
                var endMonth = period[^1].Month;
                budgets.Add((Slot.Of(endMonth.FiscalYear == annual.FiscalYear ? endMonth : MarchOf(annual.FiscalYear)), annual.Amount));
                continue;
            }

            var months = period.Where(days => days.Month.FiscalYear == annual.FiscalYear).ToList();
            if (months.Count == 0)
            {
                budgets.Add((Slot.Of(MarchOf(annual.FiscalYear)), annual.Amount));
                continue;
            }

            budgets.AddRange(AllocateWithOverrides(annual.Amount, months, overrides).Select(entry => (Slot.Of(entry.Month), entry.Amount)));
        }

        return budgets;
    }

    /// <summary>
    /// 人工。作業明細の行ごとに、その行の期間(日付が空欄なら工期)の各月へ割り振る。
    /// 工事の日付が空欄なら、作業明細の日付によらず時期未定の欄に入れる。
    /// </summary>
    internal static IEnumerable<LaborLineSlotManDays> ManDaysOf(ConstructionWork work, IReadOnlyList<MonthDays>? period, IEnumerable<LaborLine> laborLines)
    {
        foreach (var line in laborLines)
        {
            var lineDates = DatesOf(line.StartDate, line.EndDate, $"作業明細(ID {line.Id})");
            if (period is null)
            {
                yield return new LaborLineSlotManDays(line.Id, Slot.Undetermined(work.PlannedFiscalYear), line.ManDaysTenths);
                continue;
            }

            var days = lineDates is { } dates ? Period.DaysByMonth(dates.Start, dates.End) : period;
            foreach (var (month, tenths) in Proration.ByDays(line.ManDaysTenths, SharesOf(days)))
            {
                yield return new LaborLineSlotManDays(line.Id, Slot.Of(month), tenths);
            }
        }
    }

    /// <summary>
    /// months の各月に value を割り振る。修正した月はその金額で固定し、
    /// value から修正した月の合計を引いた残りを、修正していない月に暦日数の比で割り振る。
    /// 全部の月を修正したときは、修正の金額だけを使う。
    /// </summary>
    private static IEnumerable<(YearMonth Month, long Amount)> AllocateWithOverrides(
        long value, IReadOnlyList<MonthDays> months, IReadOnlyDictionary<YearMonth, long> overrides)
    {
        var free = months.Where(days => !overrides.ContainsKey(days.Month)).ToList();
        var remainder = checked(value - months.Where(days => overrides.ContainsKey(days.Month)).Sum(days => overrides[days.Month]));
        Dictionary<YearMonth, long> allocated = free.Count == 0
            ? []
            : Proration.ByDays(remainder, SharesOf(free)).ToDictionary(entry => entry.Key, entry => entry.Value);

        return months.Select(days => (days.Month, overrides.TryGetValue(days.Month, out var fixedAmount) ? fixedAmount : allocated[days.Month]));
    }

    private static List<(YearMonth Key, long Days)> SharesOf(IEnumerable<MonthDays> days) =>
        [.. days.Select(entry => (entry.Month, (long)entry.Days))];

    /// <summary>年度の3月。</summary>
    private static YearMonth MarchOf(int fiscalYear) => new(fiscalYear + 1, 3);
}
