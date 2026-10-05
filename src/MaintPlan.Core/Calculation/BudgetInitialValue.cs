using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>予算額の初期値。</summary>
public static class BudgetInitialValue
{
    /// <summary>
    /// 費用内訳の予算額の初期値(労務費+その他の費用)と、年割の初期値を求める。
    /// 労務費は、作業明細の行ごと・年度ごとに「その年度の月に割り振った人工の合計 × その年度の単価」を求め、1円未満を切り捨てて合計する。
    /// 予算額の計算に含めない人員区分の行は、労務費に入れない。
    /// 工事の日付と実施年度がどちらも空欄のときと、必要な年度の単価が登録されていないときは、初期値を出さない。
    /// </summary>
    public static BudgetInitialValueResult Calculate(PlanData plan, int costItemId) => new Calculator(plan).Calculate(costItemId);

    /// <summary>計算の対象の費用内訳すべての予算額の初期値。キーは費用内訳ID。</summary>
    public static IReadOnlyDictionary<int, BudgetInitialValueResult> CalculateAll(PlanData plan)
    {
        var calculator = new Calculator(plan);
        return CalculationTargets.CostItems(plan).ToDictionary(item => item.Id, item => calculator.Calculate(item.Id));
    }

    /// <summary>足りない単価を、人員区分の種別・表示順・年度の順に並べる。同じ人員区分・年度は1件にする。</summary>
    public static IReadOnlyList<MissingUnitRate> OrderMissingUnitRates(IEnumerable<MissingUnitRate> rates, IEnumerable<StaffCategory> categories)
    {
        var byId = categories.ToDictionary(category => category.Id);
        return [.. rates
            .Distinct()
            .OrderBy(rate => byId[rate.StaffCategoryId].Kind)
            .ThenBy(rate => byId[rate.StaffCategoryId].DisplayOrder)
            .ThenBy(rate => rate.StaffCategoryId)
            .ThenBy(rate => rate.FiscalYear)];
    }

    /// <summary>計画1つ分の索引を持ち、費用内訳ごとの初期値を求める。</summary>
    private sealed class Calculator
    {
        private readonly Dictionary<int, CostItem> items;
        private readonly Dictionary<int, ConstructionWork> works;
        private readonly ILookup<int, LaborLine> laborLines;
        private readonly Dictionary<int, StaffCategory> categories;
        private readonly Dictionary<(int StaffCategoryId, int FiscalYear), long> rates;

        public Calculator(PlanData plan)
        {
            items = CalculationTargets.CostItems(plan).ToDictionary(item => item.Id);
            works = plan.ConstructionWorks.ToDictionary(work => work.Id);
            laborLines = plan.LaborLines.Where(line => !line.IsDeleted).ToLookup(line => line.CostItemId);
            categories = plan.StaffCategories.ToDictionary(category => category.Id);
            rates = UnitRatesOf(plan);
        }

        public BudgetInitialValueResult Calculate(int costItemId)
        {
            var item = items.GetValueOrDefault(costItemId)
                ?? throw new ArgumentException($"費用内訳(ID {costItemId})は計算の対象ではありません。", nameof(costItemId));
            var work = works[item.ConstructionWorkId];
            var period = MonthlyAllocation.WorkPeriodOf(work);
            var lines = laborLines[item.Id].ToList();
            var slots = MonthlyAllocation.ManDaysOf(work, period, lines).ToList();
            var included = lines.Where(line => CategoryOf(line).IncludeInBudget).Select(line => line.Id).ToHashSet();
            if (period is null && work.PlannedFiscalYear is null)
            {
                return new BudgetInitialValueResult { IsProduced = false };
            }

            // 作業明細の行・年度ごとの人工。行の期間が1日でもかかる年度は、人工が0でも単価が要る。
            var manDays = slots
                .Where(entry => included.Contains(entry.LaborLineId))
                .GroupBy(entry => (entry.LaborLineId, FiscalYear: entry.Slot.FiscalYear!.Value))
                .Select(group => (group.Key.LaborLineId, group.Key.FiscalYear, ManDaysTenths: group.Sum(entry => entry.ManDaysTenths)))
                .ToList();
            var categoryOfLine = lines.ToDictionary(line => line.Id, line => line.StaffCategoryId);

            var missing = manDays
                .Select(entry => new MissingUnitRate(categoryOfLine[entry.LaborLineId], entry.FiscalYear))
                .Where(rate => !rates.ContainsKey((rate.StaffCategoryId, rate.FiscalYear)));
            var orderedMissing = OrderMissingUnitRates(missing, categories.Values);
            if (orderedMissing.Count > 0)
            {
                return new BudgetInitialValueResult { IsProduced = false, MissingUnitRates = orderedMissing };
            }

            var entries = manDays
                .Select(entry =>
                {
                    var rate = rates[(categoryOfLine[entry.LaborLineId], entry.FiscalYear)];
                    return new LaborCostEntry(entry.LaborLineId, entry.FiscalYear, entry.ManDaysTenths, rate, checked(entry.ManDaysTenths * rate) / 10);
                })
                .ToList();
            var laborCost = entries.Sum(entry => entry.Cost);
            var total = checked(laborCost + (item.OtherCost ?? 0));

            return new BudgetInitialValueResult
            {
                IsProduced = true,
                LaborCost = laborCost,
                Total = total,
                Allocations = AllocateToFiscalYears(item, work, period, total),
                LaborCostEntries = entries,
            };
        }

        private StaffCategory CategoryOf(LaborLine line) =>
            categories.TryGetValue(line.StaffCategoryId, out var category)
                ? category
                : throw new InvalidOperationException($"作業明細(ID {line.Id})の人員区分(ID {line.StaffCategoryId})がありません。");
    }

    /// <summary>
    /// 年割の初期値。出来高は工期の暦日数を年度ごとに数えた比で割り振り、完成工事高は工事終了日の年度に全額を入れる。
    /// 工事の日付が空欄なら、実施年度に全額を入れる。
    /// </summary>
    private static IReadOnlyList<FiscalYearAmount> AllocateToFiscalYears(CostItem item, ConstructionWork work, IReadOnlyList<MonthDays>? period, long total)
    {
        if (period is null)
        {
            return [new FiscalYearAmount(work.PlannedFiscalYear!.Value, total)];
        }

        if (item.Recognition == RecognitionMethod.Completion)
        {
            return [new FiscalYearAmount(period[^1].Month.FiscalYear, total)];
        }

        var shares = period
            .GroupBy(days => days.Month.FiscalYear)
            .Select(group => (Key: group.Key, Days: group.Sum(days => (long)days.Days)))
            .ToList();
        return [.. Proration.ByDays(total, shares).Select(entry => new FiscalYearAmount(entry.Key, entry.Value))];
    }

    /// <summary>削除済みでない単価を「(人員区分ID, 年度) → 単価」で返す。同じ人員区分・年度の単価が重複したら例外にする。</summary>
    private static Dictionary<(int StaffCategoryId, int FiscalYear), long> UnitRatesOf(PlanData plan)
    {
        var rates = new Dictionary<(int StaffCategoryId, int FiscalYear), long>();
        foreach (var rate in plan.UnitRates.Where(rate => !rate.IsDeleted))
        {
            if (!rates.TryAdd((rate.StaffCategoryId, rate.FiscalYear), rate.Rate))
            {
                throw new InvalidOperationException($"人員区分(ID {rate.StaffCategoryId})の単価で、{rate.FiscalYear}年度が重複しています。");
            }
        }

        return rates;
    }
}
