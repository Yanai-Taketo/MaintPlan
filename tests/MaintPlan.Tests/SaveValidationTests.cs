using MaintPlan.Core.Calculation;
using MaintPlan.IO.Csv;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>
/// 保存できない理由が持つ費用内訳・作業明細・年度・金額を、設計書4章の例14と例16の表の値で確かめる。
/// 保存できるかどうかと理由の種類は、期待値ファイル(保存の確認)で確かめる。費用内訳と作業明細の ID は、例14ではどちらも1。
/// 例16の費用内訳の ID は、設計書に出てくる順に、修繕費が1、設備投資が2。例16では、理由のすべてを確かめる。
/// </summary>
public class SaveValidationTests
{
    /// <summary>設計書4章の例16の表のケースごとの、保存できない理由のすべて。</summary>
    private static readonly Dictionary<string, SaveViolation[]> Example16Violations = new()
    {
        // 基本の状態は保存できる
        ["例16/基本"] = [],
        // #1 2026年度の修正の合計450,000が年割額400,000を超える
        ["例16/変更01"] = [Violation(SaveViolationKind.BudgetOverridesExceed, 1, 2026, 450000L, 400000L)],
        // #2 2027年度の修正の合計700,000が年割額600,000を超える
        ["例16/変更02"] = [Violation(SaveViolationKind.BudgetOverridesExceed, 1, 2027, 700000L, 600000L)],
        // #3 2027年度の全部の月を修正し、合計500,000が年割額600,000と一致しない
        ["例16/変更03"] = [Violation(SaveViolationKind.BudgetOverridesMismatch, 1, 2027, 500000L, 600000L)],
        // #4 2027年度の全部の月を修正し、合計600,000が年割額と一致するので、保存できる
        ["例16/変更04"] = [],
        // #5 修正の合計900,000が見積額500,000を超える
        ["例16/変更05"] = [Violation(SaveViolationKind.EstimateOverridesExceed, 2, null, 900000L, 500000L)],
        // #6 2027年度の修正の合計800,000が年割額500,000を超える
        ["例16/変更06"] = [Violation(SaveViolationKind.BudgetOverridesExceed, 2, 2027, 800000L, 500000L)],
        // #7 修繕費は、2026年度の修正の合計450,000が年割額400,000を超え、2027年度の修正の合計700,000が年割額600,000を超える。
        // 設備投資は、修正の合計900,000が見積額500,000を超える
        ["例16/変更07"] =
        [
            Violation(SaveViolationKind.BudgetOverridesExceed, 1, 2026, 450000L, 400000L),
            Violation(SaveViolationKind.BudgetOverridesExceed, 1, 2027, 700000L, 600000L),
            Violation(SaveViolationKind.EstimateOverridesExceed, 2, null, 900000L, 500000L),
        ],
    };

    public static TheoryData<string> Example16Cases => [.. Example16Violations.Keys];

    [Theory]
    // #1 作業明細の期間が工期の外にある
    [InlineData("例14/変更01", SaveViolationKind.LaborLineOutsidePeriod, 1, null, null, null)]
    // #2 作業明細に日付がある
    [InlineData("例14/変更02", SaveViolationKind.LaborLineDatedWithoutWorkDates, 1, null, null, null)]
    // #3 作業明細の期間が工期の外に出る
    [InlineData("例14/変更03", SaveViolationKind.LaborLineOutsidePeriod, 1, null, null, null)]
    // #5 修正の合計1,000,000が見積額900,000を超える
    [InlineData("例14/変更05", SaveViolationKind.EstimateOverridesExceed, null, null, 1000000L, 900000L)]
    // #6 全部の月を修正し、合計800,000が見積額(900,000)と一致しない
    [InlineData("例14/変更06", SaveViolationKind.EstimateOverridesMismatch, null, null, 800000L, 900000L)]
    // #8 修正の合計300,000が見積額(250,000)を超える
    [InlineData("例14/変更08", SaveViolationKind.EstimateOverridesExceed, null, null, 300000L, 250000L)]
    // #9 2027年度の修正の合計1,000,000が年割額900,000を超える
    [InlineData("例14/変更09", SaveViolationKind.BudgetOverridesExceed, null, 2027, 1000000L, 900000L)]
    // #10 修正の合計400,000が年割額(2027年度 300,000)を超える
    [InlineData("例14/変更10", SaveViolationKind.BudgetOverridesExceed, null, 2027, 400000L, 300000L)]
    public void Violation_has_labor_line_fiscal_year_and_amounts(
        string caseId, SaveViolationKind kind, int? laborLineId, int? fiscalYear, long? overrideTotal, long? comparedAmount)
    {
        var plan = PlanCsvReader.ReadFolder(TestCases.Get(caseId).InputDirectory);

        var violation = Assert.Single(SaveValidation.Validate(plan).Violations);

        Assert.Equal(new SaveViolation(kind, 1, laborLineId, fiscalYear, overrideTotal, comparedAmount), violation);
    }

    [Theory]
    [MemberData(nameof(Example16Cases))]
    public void All_violations_have_cost_item_fiscal_year_and_amounts(string caseId)
    {
        var plan = PlanCsvReader.ReadFolder(TestCases.Get(caseId).InputDirectory);

        var violations = SaveValidation.Validate(plan).Violations;

        // Core の結果の順は決まっていないので、順を比べない。
        Assert.Equal(Sorted(Example16Violations[caseId]), Sorted(violations));
    }

    /// <summary>見積額か予算額の理由(作業明細はなし)。引数は、理由の種類・費用内訳 ID・年度・修正の合計・比べた額。</summary>
    private static SaveViolation Violation(SaveViolationKind kind, int costItemId, int? fiscalYear, long overrideTotal, long comparedAmount) =>
        new(kind, costItemId, null, fiscalYear, overrideTotal, comparedAmount);

    /// <summary>すべての項目の順に並べる。</summary>
    private static List<SaveViolation> Sorted(IEnumerable<SaveViolation> violations) =>
        [.. violations.OrderBy(violation =>
            (violation.Kind, violation.CostItemId, violation.LaborLineId, violation.FiscalYear, violation.OverrideTotal, violation.ComparedAmount))];
}
