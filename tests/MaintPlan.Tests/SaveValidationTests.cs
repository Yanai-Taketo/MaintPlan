using MaintPlan.Core.Calculation;
using MaintPlan.IO.Csv;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>
/// 保存できない理由が持つ作業明細・年度・金額を、設計書4章の例14の表の値で確かめる。
/// 保存できるかどうかと理由の種類は、期待値ファイル(保存の確認)で確かめる。費用内訳と作業明細の ID は、例14ではどちらも1。
/// </summary>
public class SaveValidationTests
{
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
}
