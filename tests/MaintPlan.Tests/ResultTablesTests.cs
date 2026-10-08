using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Results;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>
/// 山積みと未入力の件数の表に、集計の条件の違う結果を混ぜて渡したときに、例外にすることを確かめる。
/// 例13には作業明細がないため、状態や費用区分を変えても人工は変わらない。
/// </summary>
public class ResultTablesTests
{
    private static readonly IReadOnlyList<string> AggregationColumns = ["欄", "予算額", "見積額", "人工"];

    private static readonly IReadOnlyList<string> MissingAmountColumns = ["金額の種類", "件数", "対象"];

    private static readonly PlanData Plan = PlanCsvReader.ReadFolder(TestCases.Get("例13/基本").InputDirectory);

    [Fact]
    public void Results_with_different_statuses_are_rejected_even_when_man_days_agree()
    {
        var results = new Dictionary<AmountKind, AggregationResult>
        {
            [AmountKind.Budget] = Aggregate(AmountKind.Budget, AllStatuses(), BothCategories()),
            [AmountKind.Estimate] = Aggregate(AmountKind.Estimate, new HashSet<WorkStatus> { WorkStatus.InProgress }, BothCategories()),
        };

        Assert.Throws<ArgumentException>(() => ResultTables.Aggregation(results, AggregationColumns));
        Assert.Throws<ArgumentException>(() => ResultTables.MissingAmounts(Plan, results, MissingAmountColumns));
    }

    [Fact]
    public void Results_with_different_categories_are_rejected()
    {
        var results = new Dictionary<AmountKind, AggregationResult>
        {
            [AmountKind.Budget] = Aggregate(AmountKind.Budget, AllStatuses(), BothCategories()),
            [AmountKind.Estimate] = Aggregate(AmountKind.Estimate, AllStatuses(), new HashSet<CostCategory> { CostCategory.Repair }),
        };

        Assert.Throws<ArgumentException>(() => ResultTables.Aggregation(results, AggregationColumns));
        Assert.Throws<ArgumentException>(() => ResultTables.MissingAmounts(Plan, results, MissingAmountColumns));
    }

    [Fact]
    public void Result_of_another_amount_kind_is_rejected()
    {
        var results = new Dictionary<AmountKind, AggregationResult>
        {
            [AmountKind.Budget] = Aggregate(AmountKind.Estimate, AllStatuses(), BothCategories()),
            [AmountKind.Estimate] = Aggregate(AmountKind.Estimate, AllStatuses(), BothCategories()),
        };

        Assert.Throws<ArgumentException>(() => ResultTables.Aggregation(results, AggregationColumns));
        Assert.Throws<ArgumentException>(() => ResultTables.MissingAmounts(Plan, results, MissingAmountColumns));
    }

    [Fact]
    public void Results_with_the_same_condition_are_accepted()
    {
        var results = new Dictionary<AmountKind, AggregationResult>
        {
            [AmountKind.Budget] = Aggregate(AmountKind.Budget, AllStatuses(), BothCategories()),
            [AmountKind.Estimate] = Aggregate(AmountKind.Estimate, AllStatuses(), BothCategories()),
        };

        Assert.Equal(AggregationColumns, ResultTables.Aggregation(results, AggregationColumns).Columns);
        Assert.Equal(2, ResultTables.MissingAmounts(Plan, results, MissingAmountColumns).Rows.Count);
    }

    private static AggregationResult Aggregate(AmountKind kind, IReadOnlySet<WorkStatus> statuses, IReadOnlySet<CostCategory> categories) =>
        QuarterlyAggregation.Calculate(Plan, new AggregationCondition(kind, statuses, categories));

    private static HashSet<WorkStatus> AllStatuses() => [.. Enum.GetValues<WorkStatus>()];

    private static HashSet<CostCategory> BothCategories() => [.. Enum.GetValues<CostCategory>()];
}
