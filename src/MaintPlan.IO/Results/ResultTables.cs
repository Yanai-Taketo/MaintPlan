using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;

namespace MaintPlan.IO.Results;

/// <summary>
/// 計算結果を、設計書4章の計算例の表と同じ形の表にする。
/// columns には作る表の列名を渡す。「例」「費用内訳」などの列を省くと、その列の値ごとに分けずに合計する。
/// </summary>
public static class ResultTables
{
    /// <summary>月ごとの日数(年月・日数)。最後に合計の行を置く。</summary>
    public static TextTable MonthDays(IReadOnlyList<MonthDays> days, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>月ごとの値(例・費用内訳・年月・予算額・見積額・実績額・人工)。</summary>
    public static TextTable MonthlyValues(PlanData plan, MonthlyAllocationResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>人工の月ごとの内訳(例・費用内訳・人員区分か行・年月・人工)。</summary>
    public static TextTable MonthlyManDays(PlanData plan, MonthlyAllocationResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>計算に使わない修正(例・費用内訳・金額の種類・年月・金額・理由)。</summary>
    public static TextTable UnusedOverrides(PlanData plan, MonthlyAllocationResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>予算額の初期値(例・費用内訳・労務費・予算額の初期値・年割の初期値・知らせる内容)。キーは費用内訳ID。</summary>
    public static TextTable BudgetInitialValues(PlanData plan, IReadOnlyDictionary<int, BudgetInitialValueResult> results, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>労務費の内訳(例・費用内訳・人員区分か行・年度・人工・単価・労務費)。キーは費用内訳ID。</summary>
    public static TextTable LaborCosts(PlanData plan, IReadOnlyDictionary<int, BudgetInitialValueResult> results, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>山積み(欄・予算額・見積額・実績額・人工)。金額の列は、それぞれの金額の種類で集計した結果から作る。</summary>
    public static TextTable Aggregation(IReadOnlyDictionary<AmountKind, AggregationResult> results, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>山積みの人工の内訳(欄・人員区分・人工)。</summary>
    public static TextTable LaborBreakdown(PlanData plan, AggregationResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>未入力の件数(金額の種類・件数・対象)。</summary>
    public static TextTable MissingAmounts(PlanData plan, IReadOnlyDictionary<AmountKind, AggregationResult> results, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>残予算と見込み残(年度・費用区分・予算枠・実績額・残予算・未実績見込み・見込み残)。</summary>
    public static TextTable RemainingBudget(RemainingBudgetResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>未実績見込みの内訳(例・費用内訳・年度・年度配分・未実績見込み)。</summary>
    public static TextTable UnrealizedBreakdown(PlanData plan, RemainingBudgetResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();

    /// <summary>保存の確認(保存できる・理由の種類)。</summary>
    public static TextTable SaveValidation(SaveValidationResult result, IReadOnlyList<string> columns) =>
        throw new NotImplementedException();
}
