using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>保存できない条件の確認。</summary>
public static class SaveValidation
{
    /// <summary>
    /// 対象の費用内訳について、作業明細の期間と月別修正の、保存できない条件を確かめる。
    /// 月別修正は、月への割り振りで計算に使うものだけを数える。工期に入らない月の修正、完成工事高の費用内訳の修正、
    /// 年割がない年度の予算額の修正、見積額が空欄の費用内訳の見積額の修正は数えない。
    /// </summary>
    public static SaveValidationResult Validate(PlanData plan)
    {
        var works = plan.ConstructionWorks.ToDictionary(work => work.Id);
        var annualBudgets = plan.AnnualBudgets.Where(annual => !annual.IsDeleted).ToLookup(annual => annual.CostItemId);
        var monthlyOverrides = plan.MonthlyOverrides.Where(o => !o.IsDeleted).ToLookup(o => o.CostItemId);
        var laborLines = plan.LaborLines.Where(line => !line.IsDeleted).ToLookup(line => line.CostItemId);
        var violations = new List<SaveViolation>();

        foreach (var item in CalculationTargets.CostItems(plan))
        {
            var work = works[item.ConstructionWorkId];
            var workDates = MonthlyAllocation.DatesOf(work.StartDate, work.EndDate, $"工事(ID {work.Id})");
            violations.AddRange(LaborLineViolations(item, workDates, laborLines[item.Id]));

            var period = MonthlyAllocation.WorkPeriodOf(work);
            var (overrides, _) = MonthlyAllocation.SplitOverrides(item, period, monthlyOverrides[item.Id]);
            if (item.EstimateAmount is { } estimate
                && OverrideViolationOf(estimate, period ?? [], overrides[AmountKind.Estimate], SaveViolationKind.EstimateOverridesExceed, SaveViolationKind.EstimateOverridesMismatch) is { } estimateViolation)
            {
                violations.Add(new SaveViolation(estimateViolation, item.Id, null));
            }

            var fiscalYears = new HashSet<int>();
            foreach (var annual in annualBudgets[item.Id])
            {
                if (!fiscalYears.Add(annual.FiscalYear))
                {
                    throw new InvalidOperationException($"費用内訳(ID {item.Id})の年割で、{annual.FiscalYear}年度が重複しています。");
                }

                var months = period?.Where(days => days.Month.FiscalYear == annual.FiscalYear).ToList() ?? [];
                if (OverrideViolationOf(annual.Amount, months, overrides[AmountKind.Budget], SaveViolationKind.BudgetOverridesExceed, SaveViolationKind.BudgetOverridesMismatch) is { } budgetViolation)
                {
                    violations.Add(new SaveViolation(budgetViolation, item.Id, null));
                }
            }
        }

        return new SaveValidationResult(violations);
    }

    /// <summary>
    /// 作業明細の日付の確認。日付が空欄の行は確かめない。
    /// 工事の日付が空欄なら、日付のある行を「工事の日付が空欄で作業明細に日付がある」とする。
    /// そうでなければ、期間が工期からはみ出す行を「作業明細の期間が工期の外」とする。
    /// </summary>
    private static IEnumerable<SaveViolation> LaborLineViolations(CostItem item, (DateOnly Start, DateOnly End)? workDates, IEnumerable<LaborLine> laborLines)
    {
        foreach (var line in laborLines)
        {
            if (MonthlyAllocation.DatesOf(line.StartDate, line.EndDate, $"作業明細(ID {line.Id})") is not { } lineDates)
            {
                continue;
            }

            if (workDates is not { } work)
            {
                yield return new SaveViolation(SaveViolationKind.LaborLineDatedWithoutWorkDates, item.Id, line.Id);
            }
            else if (lineDates.Start < work.Start || lineDates.End > work.End)
            {
                yield return new SaveViolation(SaveViolationKind.LaborLineOutsidePeriod, item.Id, line.Id);
            }
        }
    }

    /// <summary>
    /// months の月別修正の確認。value は見積額か年割額。修正した月がなければ確かめない。
    /// 修正の合計が value を超えるときは exceed、超えないが全部の月を修正して合計が value と一致しないときは mismatch を返す。
    /// </summary>
    private static SaveViolationKind? OverrideViolationOf(
        long value, IReadOnlyList<MonthDays> months, IReadOnlyDictionary<YearMonth, long> overrides, SaveViolationKind exceed, SaveViolationKind mismatch)
    {
        var overridden = months.Where(days => overrides.ContainsKey(days.Month)).ToList();
        if (overridden.Count == 0)
        {
            return null;
        }

        var total = overridden.Sum(days => overrides[days.Month]);
        if (total > value)
        {
            return exceed;
        }

        return overridden.Count == months.Count && total != value ? mismatch : null;
    }
}
