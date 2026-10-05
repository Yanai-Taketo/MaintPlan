using MaintPlan.IO.Csv;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>各ケースの入力を計算し、期待値ファイルと比べる。</summary>
public class CalculationTests
{
    [Theory]
    [MemberData(nameof(TestCases.ExpectedFiles), MemberType = typeof(TestCases))]
    public void Result_matches_expected(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));
        var plan = PlanCsvReader.ReadFolder(testCase.InputDirectory);

        var actual = ActualTables.Build(expected.Kind, testCase, plan, expected.Columns);

        var differences = TableComparison.Compare(expected, actual);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }
}
