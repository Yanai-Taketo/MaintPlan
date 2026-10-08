using MaintPlan.Core.Calculation;

namespace MaintPlan.Core.Model;

/// <summary>選択項目の値と、設計書での呼び名の対応。</summary>
public static class Labels
{
    private static readonly Dictionary<Type, (Enum Value, string Label)[]> Table = new()
    {
        [typeof(WorkStatus)] =
        [
            (WorkStatus.Planning, "計画中"),
            (WorkStatus.Approved, "承認済み"),
            (WorkStatus.Ordered, "発注済み"),
            (WorkStatus.InProgress, "施工中"),
            (WorkStatus.Completed, "完了"),
            (WorkStatus.Cancelled, "中止"),
        ],
        [typeof(CostCategory)] =
        [
            (CostCategory.Repair, "修繕費"),
            (CostCategory.CapitalInvestment, "設備投資"),
        ],
        [typeof(RecognitionMethod)] =
        [
            (RecognitionMethod.Progress, "出来高"),
            (RecognitionMethod.Completion, "完成工事高"),
        ],
        [typeof(AmountKind)] =
        [
            (AmountKind.Budget, "予算額"),
            (AmountKind.Estimate, "見積額"),
            (AmountKind.Actual, "実績額"),
        ],
        [typeof(StaffKind)] =
        [
            (StaffKind.Direct, "直営"),
            (StaffKind.Contractor, "協力会社"),
        ],
        [typeof(Priority)] =
        [
            (Priority.High, "高"),
            (Priority.Medium, "中"),
            (Priority.Low, "低"),
        ],
        [typeof(PlanTable)] =
        [
            (PlanTable.ConstructionWork, "工事"),
            (PlanTable.CostItem, "費用内訳"),
            (PlanTable.AnnualBudget, "予算年割"),
            (PlanTable.ActualCost, "実績"),
            (PlanTable.MonthlyOverride, "月別修正"),
            (PlanTable.LaborLine, "作業明細"),
            (PlanTable.StaffCategory, "人員区分"),
            (PlanTable.UnitRate, "単価"),
            (PlanTable.BudgetFrame, "予算枠"),
        ],
        [typeof(UnusedOverrideReason)] =
        [
            (UnusedOverrideReason.OutsidePeriod, "工期に入らない月"),
            (UnusedOverrideReason.CompletionRecognition, "完成工事高の費用内訳"),
        ],
        [typeof(SaveViolationKind)] =
        [
            (SaveViolationKind.LaborLineOutsidePeriod, "作業明細の期間が工期の外"),
            (SaveViolationKind.LaborLineDatedWithoutWorkDates, "工事の日付が空欄で作業明細に日付がある"),
            (SaveViolationKind.EstimateOverridesExceed, "見積額の修正の合計が見積額を超える"),
            (SaveViolationKind.EstimateOverridesMismatch, "見積額の全部の月の修正の合計が見積額と一致しない"),
            (SaveViolationKind.BudgetOverridesExceed, "予算額の修正の合計が年割額を超える"),
            (SaveViolationKind.BudgetOverridesMismatch, "予算額の全部の月の修正の合計が年割額と一致しない"),
        ],
    };

    /// <summary>値の呼び名を返す。</summary>
    public static string Of<T>(T value)
        where T : struct, Enum =>
        Table[typeof(T)].Single(entry => entry.Value.Equals(value)).Label;

    /// <summary>呼び名から値を読む。</summary>
    public static bool TryParse<T>(string? label, out T value)
        where T : struct, Enum
    {
        foreach (var entry in Table[typeof(T)])
        {
            if (entry.Label == label)
            {
                value = (T)entry.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>その型の呼び名をすべて返す。</summary>
    public static IReadOnlyList<string> All<T>()
        where T : struct, Enum =>
        [.. Table[typeof(T)].Select(entry => entry.Label)];
}
