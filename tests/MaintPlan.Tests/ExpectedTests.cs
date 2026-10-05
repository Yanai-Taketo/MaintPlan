using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>各ケースの期待値ファイルを読み込み、形を確かめる。</summary>
public class ExpectedTests
{
    [Theory]
    [MemberData(nameof(TestCases.Ids), MemberType = typeof(TestCases))]
    public void Case_has_expected_files(string caseId)
    {
        Assert.NotEmpty(TestCases.Get(caseId).ExpectedFileNames);
    }

    [Theory]
    [MemberData(nameof(TestCases.ExpectedFiles), MemberType = typeof(TestCases))]
    public void Expected_file_can_be_read(string caseId, string fileName)
    {
        var testCase = TestCases.Get(caseId);
        var expected = ExpectedTable.Load(Path.Combine(testCase.ExpectedDirectory, fileName));

        if (expected.Kind.Name is "山積み" or "人工の内訳" or "未入力の件数")
        {
            Assert.NotEmpty(testCase.Statuses);
            Assert.NotEmpty(testCase.Categories);
        }

        if (expected.Kind.Name is "残予算と見込み残" or "未実績見込みの内訳")
        {
            _ = testCase.BaseDate;
        }
    }
}
