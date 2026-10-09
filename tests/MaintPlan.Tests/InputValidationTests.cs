using System.Text;
using MaintPlan.Core.Calculation;
using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.IO.Input;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>
/// 入力の確認で、3章の決まり・参照先・保存できない条件に合わない行を、ファイル・行・列とともにすべて示すことを確かめる。
/// 決まりの確認は、例01-04の入力を写して変え、期待する行と理由を、変えた内容と3章の決まりから決める。
/// 保存できない条件は、設計書4章の例14と例16の表の理由と値で確かめる。例14では、示す先の作業明細・費用内訳・予算年割のファイルは行が1つなので、
/// 示す行は2行目になる。例16では、ID を設計書に出てくる順に振る(tests/data/README.md)ので、費用内訳は修繕費が2行目・設備投資が3行目、
/// 予算年割は修繕費の2026年度が2行目・2027年度が3行目、設備投資の2026年度が4行目になる。
/// 行を足したときと、ほかの違反を足したときも、例14の入力を写して変える。
/// </summary>
public sealed class InputValidationTests : IDisposable
{
    /// <summary>設計書4章の例14と例16で、保存できないケース。</summary>
    private static readonly string[] CasesThatCannotBeSaved =
    [
        "例14/変更01", "例14/変更02", "例14/変更03", "例14/変更05", "例14/変更06", "例14/変更08", "例14/変更09", "例14/変更10",
        "例16/変更01", "例16/変更02", "例16/変更03", "例16/変更05", "例16/変更06", "例16/変更07",
    ];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "MaintPlan.Tests", Guid.NewGuid().ToString("N"));

    public InputValidationTests()
    {
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(TestCases.Get("例01-04/基本").InputDirectory))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    public static TheoryData<string> CasesThatCanBeSaved => [.. TestCases.All.Select(testCase => testCase.Id).Except(CasesThatCannotBeSaved)];

    [Theory]
    [MemberData(nameof(CasesThatCanBeSaved))]
    public void Inputs_of_examples_have_no_violations(string caseId)
    {
        var plan = PlanCsvReader.ReadFolder(TestCases.Get(caseId).InputDirectory);

        Assert.Empty(InputValidation.Validate(plan).Violations);
    }

    [Theory]
    // #1 作業明細の期間を2027-06-15〜2027-07-10にする
    [InlineData("例14/変更01", SaveViolationKind.LaborLineOutsidePeriod, "作業明細.csv 2行目 列「開始日」「終了日」: 作業明細の期間 2027-06-15〜2027-07-10 が、工期 2027-04-01〜2027-06-30 の外にあります。")]
    // #2 工事の開始日と終了日を空欄にする
    [InlineData("例14/変更02", SaveViolationKind.LaborLineDatedWithoutWorkDates, "作業明細.csv 2行目 列「開始日」「終了日」: 工事の日付が空欄で、作業明細に日付があります。")]
    // #3 工期を2027-05-15〜2027-08-31にする
    [InlineData("例14/変更03", SaveViolationKind.LaborLineOutsidePeriod, "作業明細.csv 2行目 列「開始日」「終了日」: 作業明細の期間 2027-05-01〜2027-05-31 が、工期 2027-05-15〜2027-08-31 の外にあります。")]
    // #5 修正の合計1,000,000が見積額900,000を超える
    [InlineData("例14/変更05", SaveViolationKind.EstimateOverridesExceed, "費用内訳.csv 2行目 列「見積額」: 見積額の修正の合計 1,000,000 が、見積額 900,000 を超えています。")]
    // #6 全部の月を修正し、合計800,000が見積額と一致しない
    [InlineData("例14/変更06", SaveViolationKind.EstimateOverridesMismatch, "費用内訳.csv 2行目 列「見積額」: 見積額の全部の月を修正していて、修正の合計 800,000 が見積額 900,000 と一致しません。")]
    // #8 修正の合計300,000が見積額(250,000)を超える
    [InlineData("例14/変更08", SaveViolationKind.EstimateOverridesExceed, "費用内訳.csv 2行目 列「見積額」: 見積額の修正の合計 300,000 が、見積額 250,000 を超えています。")]
    // #9 2027年度の修正の合計1,000,000が年割額900,000を超える
    [InlineData("例14/変更09", SaveViolationKind.BudgetOverridesExceed, "予算年割.csv 2行目 列「予算額」: 2027年度の予算額の修正の合計 1,000,000 が、年割額 900,000 を超えています。")]
    // #10 修正の合計400,000が年割額(2027年度 300,000)を超える
    [InlineData("例14/変更10", SaveViolationKind.BudgetOverridesExceed, "予算年割.csv 2行目 列「予算額」: 2027年度の予算額の修正の合計 400,000 が、年割額 300,000 を超えています。")]
    // 例16の#3 2027年度の全部の月を修正し、合計500,000が年割額600,000と一致しない。修繕費の2027年度の年割は3行目
    [InlineData("例16/変更03", SaveViolationKind.BudgetOverridesMismatch, "予算年割.csv 3行目 列「予算額」: 2027年度の予算額の全部の月を修正していて、修正の合計 500,000 が年割額 600,000 と一致しません。")]
    public void Save_violation_is_shown_at_its_row(string caseId, SaveViolationKind kind, string text)
    {
        var read = PlanCsvReader.Read(TestCases.Get(caseId).InputDirectory);

        var violation = Assert.Single(InputValidation.Validate(read.Plan!).Violations);

        Assert.Equal(InputViolationKind.CannotSave, violation.Kind);
        Assert.Equal(kind, violation.SaveViolation?.Kind);
        Assert.Equal(text, InputViolationText.Describe(violation, read.Source!));
    }

    [Theory]
    // ID は、削除済みの行も含めて重複なし。足す行は削除済みにし、ほかの決まりに当たらないようにする
    [InlineData("工事", "1,例9,ポンプ更新,施工中,,,,,,,,,,,はい", 6)]
    [InlineData("費用内訳", "1,1,設備投資,出来高,,,,,はい", 6)]
    [InlineData("予算年割", "1,1,2028,1,はい", 7)]
    [InlineData("実績", "1,1,2027-04,1,,はい", 4)]
    [InlineData("月別修正", "1,1,予算額,2027-04,1,はい", 3)]
    [InlineData("作業明細", "1,1,1,,,,1,1.0,はい", 7)]
    [InlineData("人員区分", "1,直営,電気,はい,3,はい,はい", 4)]
    [InlineData("単価", "1,1,2028,1,はい", 6)]
    [InlineData("予算枠", "1,2028,修繕費,1,はい", 5)]
    public void Duplicate_id_is_shown_in_every_table(string table, string line, int lineNumber)
    {
        AppendLine(table, line);

        Assert.Equal([$"{table}.csv {lineNumber}行目 列「ID」: ID 1 が重複しています。最初の行は2行目です。"], Describe());
    }

    [Theory]
    // 管理番号は、削除済みの行も含めて重複なし
    [InlineData("工事", "5,例1,ポンプ更新,施工中,2027-02-10,2027-05-24,,,,,,,,,はい",
        "工事.csv 6行目 列「管理番号」: 管理番号「例1」が重複しています。最初の行は2行目です。")]
    // 1件の工事に同じ費用区分は1件まで
    [InlineData("費用内訳", "5,1,修繕費,出来高,,100,,,いいえ",
        "費用内訳.csv 6行目 列「工事ID」「費用区分」: 工事ID 1 と費用区分「修繕費」の組み合わせが重複しています。最初の行は2行目です。")]
    // 1件の費用内訳に同じ年度は1件まで
    [InlineData("予算年割", "6,1,2026,1,いいえ",
        "予算年割.csv 7行目 列「費用内訳ID」「年度」: 費用内訳ID 1 と年度 2026 の組み合わせが重複しています。最初の行は2行目です。")]
    // 費用内訳・金額の種類・年月の組み合わせで1件
    [InlineData("月別修正", "2,1,見積額,2027-03,100,いいえ",
        "月別修正.csv 3行目 列「費用内訳ID」「金額の種類」「年月」: 費用内訳ID 1・金額の種類「見積額」・年月 2027-03 の組み合わせが重複しています。最初の行は2行目です。")]
    // 種別と区分名の組み合わせで重複なし
    [InlineData("人員区分", "3,直営,一般,はい,3,はい,いいえ",
        "人員区分.csv 4行目 列「種別」「区分名」: 種別「直営」と区分名「一般」の組み合わせが重複しています。最初の行は2行目です。")]
    // 人員区分と年度の組み合わせで1件
    [InlineData("単価", "5,1,2027,1,いいえ",
        "単価.csv 6行目 列「人員区分ID」「年度」: 人員区分ID 1 と年度 2027 の組み合わせが重複しています。最初の行は3行目です。")]
    // 年度と費用区分の組み合わせで1件
    [InlineData("予算枠", "4,2027,設備投資,1,いいえ",
        "予算枠.csv 5行目 列「年度」「費用区分」: 年度 2027 と費用区分「設備投資」の組み合わせが重複しています。最初の行は4行目です。")]
    public void Duplicate_is_shown_at_the_later_row(string table, string line, string text)
    {
        AppendLine(table, line);

        Assert.Equal([text], Describe());
    }

    [Fact]
    public void Every_later_duplicate_points_to_the_first_row()
    {
        AppendLine("予算枠", "4,2026,修繕費,1,いいえ");
        AppendLine("予算枠", "5,2026,修繕費,2,いいえ");

        Assert.Equal(
        [
            "予算枠.csv 5行目 列「年度」「費用区分」: 年度 2026 と費用区分「修繕費」の組み合わせが重複しています。最初の行は2行目です。",
            "予算枠.csv 6行目 列「年度」「費用区分」: 年度 2026 と費用区分「修繕費」の組み合わせが重複しています。最初の行は2行目です。",
        ], Describe());
    }

    [Fact]
    public void Line_numbers_count_cells_with_line_breaks()
    {
        ReplaceCell("工事", 2, "備考", "\"一行目\n二行目\"");
        AppendLine("工事", "5,例2,受変電設備更新,発注済み,2026-12-01,2027-06-30,,,,,,,,,はい");

        Assert.Equal(["工事.csv 7行目 列「管理番号」: 管理番号「例2」が重複しています。最初の行は4行目です。"], Describe());
    }

    [Fact]
    public void Work_number_is_unique_when_entered()
    {
        AppendLine("費用内訳", "5,2,修繕費,出来高,W-1,,,,いいえ");
        AppendLine("費用内訳", "6,3,設備投資,出来高,W-1,,,,いいえ");
        AppendLine("費用内訳", "7,4,設備投資,出来高,,,,,いいえ");

        Assert.Equal(["費用内訳.csv 7行目 列「工事番号」: 工事番号「W-1」が重複しています。最初の行は6行目です。"], Describe());
    }

    [Theory]
    [InlineData("費用内訳", "5,1,修繕費,出来高,,100,,,はい")]
    [InlineData("予算年割", "6,1,2026,1,はい")]
    [InlineData("月別修正", "2,1,見積額,2027-03,100,はい")]
    [InlineData("人員区分", "3,直営,一般,はい,3,はい,はい")]
    [InlineData("単価", "5,1,2027,1,はい")]
    [InlineData("予算枠", "4,2027,設備投資,1,はい")]
    public void Deleted_rows_are_not_counted_for_combinations(string table, string line)
    {
        AppendLine(table, line);

        Assert.Empty(Describe());
    }

    [Fact]
    public void Duplicate_unit_rates_of_a_deleted_staff_category_are_shown()
    {
        // 単価は計算で使うので、人員区分1が削除済みでも確かめる。
        ReplaceCell("人員区分", 2, PlanCsvReader.Deleted, "はい");
        AppendLine("単価", "5,1,2027,1,いいえ");

        Assert.Equal(["単価.csv 6行目 列「人員区分ID」「年度」: 人員区分ID 1 と年度 2027 の組み合わせが重複しています。最初の行は3行目です。"], Describe());
    }

    [Theory]
    [InlineData("工事", 2, "終了日", "", "工事.csv 2行目 列「開始日」「終了日」: 開始日と終了日は、両方入れるか両方空欄にします。")]
    [InlineData("作業明細", 3, "開始日", "", "作業明細.csv 3行目 列「開始日」「終了日」: 開始日と終了日は、両方入れるか両方空欄にします。")]
    [InlineData("工事", 5, "終了日", "2026-10-31", "工事.csv 5行目 列「開始日」「終了日」: 終了日 2026-10-31 が開始日 2026-11-01 より前です。")]
    [InlineData("作業明細", 2, "終了日", "2027-02-28", "作業明細.csv 2行目 列「開始日」「終了日」: 終了日 2027-02-28 が開始日 2027-03-01 より前です。")]
    public void Wrong_dates_are_shown(string table, int lineNumber, string column, string value, string text)
    {
        ReplaceCell(table, lineNumber, column, value);

        Assert.Equal([text], Describe());
    }

    [Fact]
    public void Dates_of_deleted_rows_are_not_checked()
    {
        ReplaceCell("作業明細", 3, "開始日", "");
        ReplaceCell("作業明細", 3, PlanCsvReader.Deleted, "はい");

        Assert.Empty(Describe());
    }

    [Theory]
    [InlineData("費用内訳", "工事ID", "費用内訳.csv 2行目 列「工事ID」: 工事ID 9 に当たる工事の行がありません。")]
    [InlineData("予算年割", "費用内訳ID", "予算年割.csv 2行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。")]
    [InlineData("実績", "費用内訳ID", "実績.csv 2行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。")]
    [InlineData("月別修正", "費用内訳ID", "月別修正.csv 2行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。")]
    [InlineData("作業明細", "費用内訳ID", "作業明細.csv 2行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。")]
    [InlineData("作業明細", "人員区分ID", "作業明細.csv 2行目 列「人員区分ID」: 人員区分ID 9 に当たる人員区分の行がありません。")]
    [InlineData("単価", "人員区分ID", "単価.csv 2行目 列「人員区分ID」: 人員区分ID 9 に当たる人員区分の行がありません。")]
    public void Missing_reference_is_shown(string table, string column, string text)
    {
        ReplaceCell(table, 2, column, "9");

        Assert.Equal([text], Describe());
    }

    [Fact]
    public void Missing_reference_of_a_deleted_row_is_shown()
    {
        ReplaceCell("実績", 2, "費用内訳ID", "9");
        ReplaceCell("実績", 2, PlanCsvReader.Deleted, "はい");

        Assert.Equal(["実績.csv 2行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。"], Describe());
    }

    [Theory]
    [InlineData("工事")]
    [InlineData("費用内訳")]
    [InlineData("人員区分")]
    public void Rows_may_refer_to_deleted_rows(string table)
    {
        ReplaceCell(table, 2, PlanCsvReader.Deleted, "はい");

        Assert.Empty(Describe());
    }

    [Fact]
    public void Rows_under_deleted_work_and_cost_item_are_not_checked()
    {
        AddRowsUnderWorkAndCostItem(deleted: "はい");

        Assert.Empty(Describe());
    }

    [Fact]
    public void Rows_under_work_and_cost_item_that_are_not_deleted_are_checked()
    {
        AddRowsUnderWorkAndCostItem(deleted: "いいえ");

        Assert.Equal(
        [
            "費用内訳.csv 6行目 列「工事番号」: 工事番号「W-1」が重複しています。最初の行は2行目です。",
            "費用内訳.csv 7行目 列「工事ID」「費用区分」: 工事ID 5 と費用区分「修繕費」の組み合わせが重複しています。最初の行は6行目です。",
            "予算年割.csv 8行目 列「費用内訳ID」「年度」: 費用内訳ID 5 と年度 2027 の組み合わせが重複しています。最初の行は7行目です。",
            "予算年割.csv 10行目 列「費用内訳ID」「年度」: 費用内訳ID 7 と年度 2027 の組み合わせが重複しています。最初の行は9行目です。",
            "月別修正.csv 4行目 列「費用内訳ID」「金額の種類」「年月」: 費用内訳ID 5・金額の種類「予算額」・年月 2027-05 の組み合わせが重複しています。最初の行は3行目です。",
            "月別修正.csv 6行目 列「費用内訳ID」「金額の種類」「年月」: 費用内訳ID 7・金額の種類「見積額」・年月 2027-05 の組み合わせが重複しています。最初の行は5行目です。",
            "作業明細.csv 7行目 列「開始日」「終了日」: 開始日と終了日は、両方入れるか両方空欄にします。",
            "作業明細.csv 8行目 列「開始日」「終了日」: 終了日 2027-05-01 が開始日 2027-05-31 より前です。",
        ], Describe());
    }

    [Fact]
    public void Id_duplicates_and_missing_references_under_deleted_parents_are_shown()
    {
        // 工事4と費用内訳3を削除済みにする。それぞれの下に、ID の重なる行(費用内訳4の年割、費用内訳3の作業明細)を足し、
        // 作業明細(費用内訳3の作業明細4、費用内訳4の作業明細5)の人員区分を、ない ID にする。
        ReplaceCell("工事", 5, PlanCsvReader.Deleted, "はい");
        ReplaceCell("費用内訳", 4, PlanCsvReader.Deleted, "はい");
        AppendLine("予算年割", "1,4,2027,1,いいえ");
        AppendLine("作業明細", "1,3,1,,,,1,1.0,いいえ");
        ReplaceCell("作業明細", 5, "人員区分ID", "9");
        ReplaceCell("作業明細", 6, "人員区分ID", "9");

        Assert.Equal(
        [
            "予算年割.csv 7行目 列「ID」: ID 1 が重複しています。最初の行は2行目です。",
            "作業明細.csv 5行目 列「人員区分ID」: 人員区分ID 9 に当たる人員区分の行がありません。",
            "作業明細.csv 6行目 列「人員区分ID」: 人員区分ID 9 に当たる人員区分の行がありません。",
            "作業明細.csv 7行目 列「ID」: ID 1 が重複しています。最初の行は2行目です。",
        ], Describe());
    }

    [Theory]
    // 先の行を削除済みにする
    [InlineData("はい", "いいえ")]
    // 後の行を削除済みにする
    [InlineData("いいえ", "はい")]
    public void Parent_with_a_row_that_is_not_deleted_is_not_treated_as_deleted(string firstDeleted, string laterDeleted)
    {
        // 工事4と費用内訳1に同じ ID の行を足し、どちらか一方だけを削除済みにする。工事4の下に費用区分が同じ費用内訳、費用内訳1の下に年度が同じ年割を足す。
        ReplaceCell("工事", 5, PlanCsvReader.Deleted, firstDeleted);
        AppendLine("工事", $"4,例5,配管補修,承認済み,2026-11-01,2026-12-15,,,,,,,,,{laterDeleted}");
        ReplaceCell("費用内訳", 2, PlanCsvReader.Deleted, firstDeleted);
        AppendLine("費用内訳", $"1,1,修繕費,出来高,,3000000,500000,,{laterDeleted}");
        AppendLine("費用内訳", "5,4,修繕費,出来高,,,,,いいえ");
        AppendLine("予算年割", "6,1,2026,1,いいえ");

        Assert.Equal(
        [
            "工事.csv 6行目 列「ID」: ID 4 が重複しています。最初の行は5行目です。",
            "費用内訳.csv 6行目 列「ID」: ID 1 が重複しています。最初の行は2行目です。",
            "費用内訳.csv 7行目 列「工事ID」「費用区分」: 工事ID 4 と費用区分「修繕費」の組み合わせが重複しています。最初の行は5行目です。",
            "予算年割.csv 7行目 列「費用内訳ID」「年度」: 費用内訳ID 1 と年度 2026 の組み合わせが重複しています。最初の行は2行目です。",
        ], Describe());
    }

    [Fact]
    public void Cost_item_whose_rows_are_all_treated_as_deleted_is_a_deleted_parent()
    {
        // 工事4を削除済みにし、費用内訳4の行を、工事1の削除済みの行として足す。費用内訳4の行はどちらも削除済みとして扱うので、その下の年割の重複は確かめない。
        ReplaceCell("工事", 5, PlanCsvReader.Deleted, "はい");
        AppendLine("費用内訳", "4,1,設備投資,出来高,,,,,はい");
        AppendLine("予算年割", "6,4,2026,1,いいえ");

        Assert.Equal(["費用内訳.csv 6行目 列「ID」: ID 4 が重複しています。最初の行は5行目です。"], Describe());
    }

    [Fact]
    public void Cost_item_with_a_row_under_a_work_that_is_not_deleted_is_not_treated_as_deleted()
    {
        // 工事4を削除済みにし、費用内訳4の行を、工事1の削除済みでない行として足す。費用内訳4は削除済みとして扱わないので、その下の年割の重複を確かめる。
        ReplaceCell("工事", 5, PlanCsvReader.Deleted, "はい");
        AppendLine("費用内訳", "4,1,設備投資,出来高,,,,,いいえ");
        AppendLine("予算年割", "6,4,2026,1,いいえ");

        Assert.Equal(
        [
            "費用内訳.csv 6行目 列「ID」: ID 4 が重複しています。最初の行は5行目です。",
            "予算年割.csv 7行目 列「費用内訳ID」「年度」: 費用内訳ID 4 と年度 2026 の組み合わせが重複しています。最初の行は6行目です。",
        ], Describe());
    }

    [Fact]
    public void Row_under_a_deleted_work_is_not_the_first_row_of_a_work_number()
    {
        // 工事4を削除済みにし、その費用内訳4に工事番号 W-1 を入れる。後に、工事2の費用内訳として同じ工事番号の行を足す。
        ReplaceCell("工事", 5, PlanCsvReader.Deleted, "はい");
        ReplaceCell("費用内訳", 5, "工事番号", "W-1");
        AppendLine("費用内訳", "5,2,修繕費,出来高,W-1,,,,いいえ");

        Assert.Empty(Describe());
    }

    [Fact]
    public void Rows_under_a_missing_work_are_checked()
    {
        // 工事9の行はない。工事9の費用内訳5と同じ工事番号の費用内訳を後に足し、費用内訳5の下に年度が同じ年割を2件足す。
        AppendLine("費用内訳", "5,9,修繕費,出来高,W-1,,,,いいえ");
        AppendLine("費用内訳", "6,2,修繕費,出来高,W-1,,,,いいえ");
        AppendLine("予算年割", "6,5,2027,1,いいえ");
        AppendLine("予算年割", "7,5,2027,1,いいえ");

        Assert.Equal(
        [
            "費用内訳.csv 6行目 列「工事ID」: 工事ID 9 に当たる工事の行がありません。",
            "費用内訳.csv 7行目 列「工事番号」: 工事番号「W-1」が重複しています。最初の行は6行目です。",
            "予算年割.csv 8行目 列「費用内訳ID」「年度」: 費用内訳ID 5 と年度 2027 の組み合わせが重複しています。最初の行は7行目です。",
        ], Describe());
    }

    [Fact]
    public void Rows_under_a_missing_cost_item_are_checked()
    {
        // 費用内訳9の行はない。費用内訳9の月別修正を2件、同じ組み合わせにする。
        // 後の行には、組み合わせの重複と参照先の違反が出る。同じ行の中の違反の順は決まっていないので、順を比べない。
        ReplaceCell("月別修正", 2, "費用内訳ID", "9");
        AppendLine("月別修正", "2,9,見積額,2027-03,100,いいえ");
        string[] expected =
        [
            "月別修正.csv 2行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。",
            "月別修正.csv 3行目 列「費用内訳ID」「金額の種類」「年月」: 費用内訳ID 9・金額の種類「見積額」・年月 2027-03 の組み合わせが重複しています。最初の行は2行目です。",
            "月別修正.csv 3行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Describe().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Override_of_actual_amount_is_shown()
    {
        var plan = PlanCsvReader.ReadFolder(directory);
        plan = plan with { MonthlyOverrides = [plan.MonthlyOverrides[0] with { Kind = AmountKind.Actual }] };

        var violation = Assert.Single(InputValidation.Validate(plan).Violations);

        Assert.Equal((InputViolationKind.InvalidOverrideKind, PlanTable.MonthlyOverride, 0), (violation.Kind, violation.Table, violation.RowIndex));
        Assert.Equal(["金額の種類"], violation.Columns);
    }

    [Theory]
    [InlineData(PlanTable.ConstructionWork)]
    [InlineData(PlanTable.CostItem)]
    public void Override_of_actual_amount_under_a_deleted_parent_is_not_shown(PlanTable deletedParent)
    {
        // 月別修正1は、工事1の費用内訳1の修正。工事1か費用内訳1を削除済みにする。
        var plan = PlanCsvReader.ReadFolder(directory);
        plan = plan with
        {
            ConstructionWorks = [plan.ConstructionWorks[0] with { IsDeleted = deletedParent == PlanTable.ConstructionWork }, .. plan.ConstructionWorks.Skip(1)],
            CostItems = [plan.CostItems[0] with { IsDeleted = deletedParent == PlanTable.CostItem }, .. plan.CostItems.Skip(1)],
            MonthlyOverrides = [plan.MonthlyOverrides[0] with { Kind = AmountKind.Actual }],
        };

        Assert.Empty(InputValidation.Validate(plan).Violations);
    }

    [Fact]
    public void Violations_are_shown_in_table_and_row_order()
    {
        ReplaceCell("作業明細", 3, "費用内訳ID", "9");
        ReplaceCell("作業明細", 2, "人員区分ID", "9");
        ReplaceCell("工事", 5, "終了日", "");

        Assert.Equal(
        [
            "工事.csv 5行目 列「開始日」「終了日」: 開始日と終了日は、両方入れるか両方空欄にします。",
            "作業明細.csv 2行目 列「人員区分ID」: 人員区分ID 9 に当たる人員区分の行がありません。",
            "作業明細.csv 3行目 列「費用内訳ID」: 費用内訳ID 9 に当たる費用内訳の行がありません。",
        ], Describe());
    }

    [Fact]
    public void Rules_not_in_the_design_are_not_checked()
    {
        // 日付と実施年度の両方、101文字の工事名、前に空白のある管理番号、負の見積額、費用内訳のない工事
        ReplaceCell("工事", 2, "実施年度", "2026");
        ReplaceCell("工事", 2, "工事名", new string('あ', 101));
        ReplaceCell("工事", 3, "管理番号", " 例2");
        ReplaceCell("費用内訳", 3, "見積額", "-1");
        AppendLine("工事", "5,例5,配管補修,計画中,,,,,,,,,,,いいえ");

        Assert.Empty(Describe());
    }

    [Fact]
    public void Budget_violation_is_shown_at_the_annual_budget_of_its_fiscal_year()
    {
        // 例14の#9に、修正のない2026年度の年割を先に足す。
        UseInput("例14/変更09");
        ReplaceRows("予算年割", "2,1,2026,100000,いいえ", "1,1,2027,900000,いいえ");

        Assert.Equal(["予算年割.csv 3行目 列「予算額」: 2027年度の予算額の修正の合計 1,000,000 が、年割額 900,000 を超えています。"], Describe());
    }

    [Fact]
    public void Budget_violation_is_not_shown_at_a_deleted_annual_budget()
    {
        // 例14の#9に、2027年度の削除済みの年割を先に足す。
        UseInput("例14/変更09");
        ReplaceRows("予算年割", "2,1,2027,500000,はい", "1,1,2027,900000,いいえ");

        Assert.Equal(["予算年割.csv 3行目 列「予算額」: 2027年度の予算額の修正の合計 1,000,000 が、年割額 900,000 を超えています。"], Describe());
    }

    [Fact]
    public void Every_save_violation_is_shown_at_its_row()
    {
        // 例16の#7(#1・#2・#5を同時に行う)。設備投資の見積額の理由は費用内訳の3行目、修繕費の2026年度と2027年度の予算額の理由は予算年割の2行目と3行目。
        // 違反は、テーブルの順、行の順に示す。
        UseInput("例16/変更07");

        Assert.Equal(
        [
            "費用内訳.csv 3行目 列「見積額」: 見積額の修正の合計 900,000 が、見積額 500,000 を超えています。",
            "予算年割.csv 2行目 列「予算額」: 2026年度の予算額の修正の合計 450,000 が、年割額 400,000 を超えています。",
            "予算年割.csv 3行目 列「予算額」: 2027年度の予算額の修正の合計 700,000 が、年割額 600,000 を超えています。",
        ], Describe());
    }

    [Fact]
    public void Labor_line_violation_is_shown_at_its_labor_line()
    {
        // 例14の#1に、工期の中の作業明細を先に足す。
        UseInput("例14/変更01");
        ReplaceRows("作業明細", "2,1,1,,2027-05-01,2027-05-31,1,3.0,いいえ", "1,1,1,,2027-06-15,2027-07-10,1,3.0,いいえ");

        Assert.Equal(["作業明細.csv 3行目 列「開始日」「終了日」: 作業明細の期間 2027-06-15〜2027-07-10 が、工期 2027-04-01〜2027-06-30 の外にあります。"], Describe());
    }

    [Fact]
    public void Duplicate_management_number_does_not_hide_save_violations()
    {
        // 例14の#1に、同じ管理番号の削除済みの工事を足す。
        UseInput("例14/変更01");
        AppendLine("工事", "2,例14,給湯配管更新,計画中,,,,,,,,,,,はい");

        Assert.Equal(
        [
            "工事.csv 3行目 列「管理番号」: 管理番号「例14」が重複しています。最初の行は2行目です。",
            "作業明細.csv 2行目 列「開始日」「終了日」: 作業明細の期間 2027-06-15〜2027-07-10 が、工期 2027-04-01〜2027-06-30 の外にあります。",
        ], Describe());
    }

    [Fact]
    public void Duplicate_override_does_not_hide_labor_line_violations()
    {
        // 例14の#1に、見積額の2027-04の修正を重ねて足す。見積額の修正は確かめないが、作業明細の理由は示す。
        UseInput("例14/変更01");
        AppendLine("月別修正", "3,1,見積額,2027-04,1,いいえ");

        Assert.Equal(
        [
            "月別修正.csv 4行目 列「費用内訳ID」「金額の種類」「年月」: 費用内訳ID 1・金額の種類「見積額」・年月 2027-04 の組み合わせが重複しています。最初の行は2行目です。",
            "作業明細.csv 2行目 列「開始日」「終了日」: 作業明細の期間 2027-06-15〜2027-07-10 が、工期 2027-04-01〜2027-06-30 の外にあります。",
        ], Describe());
    }

    [Fact]
    public void Duplicate_annual_budget_does_not_hide_estimate_violations()
    {
        // 例14の#5に、2027年度の年割を重ねて足す。予算額の修正は確かめないが、見積額の理由は示す。
        UseInput("例14/変更05");
        AppendLine("予算年割", "2,1,2027,1,いいえ");

        Assert.Equal(
        [
            "費用内訳.csv 2行目 列「見積額」: 見積額の修正の合計 1,000,000 が、見積額 900,000 を超えています。",
            "予算年割.csv 3行目 列「費用内訳ID」「年度」: 費用内訳ID 1 と年度 2027 の組み合わせが重複しています。最初の行は2行目です。",
        ], Describe());
    }

    [Fact]
    public void Duplicate_id_of_a_deleted_override_does_not_hide_save_violations()
    {
        // 例14の#5に、ID の重なる削除済みの月別修正を足す。
        UseInput("例14/変更05");
        AppendLine("月別修正", "1,1,予算額,2027-05,5,はい");

        Assert.Equal(
        [
            "費用内訳.csv 2行目 列「見積額」: 見積額の修正の合計 1,000,000 が、見積額 900,000 を超えています。",
            "月別修正.csv 5行目 列「ID」: ID 1 が重複しています。最初の行は2行目です。",
        ], Describe());
    }

    /// <summary>入力を読んで確かめ、違反を文にして返す。</summary>
    private List<string> Describe()
    {
        var read = PlanCsvReader.Read(directory);
        Assert.Empty(read.Errors);
        return [.. InputValidation.Validate(read.Plan!).Violations.Select(violation => InputViolationText.Describe(violation, read.Source!))];
    }

    /// <summary>
    /// 工事5と、工事3の費用内訳7を、削除済みの印を deleted にして足す。その下に、削除済みの印がなく、親が削除済みでなければ決まりに合わない行を足す。
    /// 工事5の下は、費用内訳1と同じ工事番号の費用内訳5と、費用内訳5と費用区分が同じ費用内訳6。費用内訳5と7の下は、年度が同じ年割、
    /// 組み合わせが同じ月別修正と、日付の誤った作業明細(費用内訳5は開始日だけ、費用内訳7は終了日が開始日より前)。
    /// 親が削除済みでないときも、保存できない条件には当たらない(日付の誤った行と重複した年割・月別修正は、保存できない条件の確認から除くため)。
    /// </summary>
    private void AddRowsUnderWorkAndCostItem(string deleted)
    {
        ReplaceCell("費用内訳", 2, "工事番号", "W-1");
        AppendLine("工事", $"5,例5,配管更新,計画中,2027-04-01,2027-09-30,,,,,,,,,{deleted}");
        AppendLine("費用内訳", "5,5,修繕費,出来高,W-1,,,,いいえ");
        AppendLine("費用内訳", "6,5,修繕費,出来高,,,,,いいえ");
        AppendLine("費用内訳", $"7,3,設備投資,出来高,,,,,{deleted}");
        AppendLine("予算年割", "6,5,2027,100,いいえ");
        AppendLine("予算年割", "7,5,2027,100,いいえ");
        AppendLine("予算年割", "8,7,2027,100,いいえ");
        AppendLine("予算年割", "9,7,2027,100,いいえ");
        AppendLine("月別修正", "2,5,予算額,2027-05,100,いいえ");
        AppendLine("月別修正", "3,5,予算額,2027-05,100,いいえ");
        AppendLine("月別修正", "4,7,見積額,2027-05,100,いいえ");
        AppendLine("月別修正", "5,7,見積額,2027-05,100,いいえ");
        AppendLine("作業明細", "6,5,1,,2027-05-01,,1,1.0,いいえ");
        AppendLine("作業明細", "7,7,1,,2027-05-31,2027-05-01,1,1.0,いいえ");
    }

    /// <summary>写した入力を、caseId のケースの入力に入れ替える。</summary>
    private void UseInput(string caseId)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }

        foreach (var file in Directory.EnumerateFiles(TestCases.Get(caseId).InputDirectory))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
    }

    /// <summary>列名の行を残し、データの行を rows に置き換える。</summary>
    private void ReplaceRows(string table, params string[] rows)
    {
        var path = Path.Combine(directory, table + ".csv");
        File.WriteAllLines(path, [File.ReadLines(path).First(), .. rows], new UTF8Encoding(true));
    }

    private void AppendLine(string table, string line) =>
        File.AppendAllText(Path.Combine(directory, table + ".csv"), line + "\n", new UTF8Encoding(false));

    /// <summary>ファイルの lineNumber 行目(1行目が列名)の、column の列の値を置き換える。改行は CRLF でも LF でもよい。</summary>
    private void ReplaceCell(string table, int lineNumber, string column, string value)
    {
        var path = Path.Combine(directory, table + ".csv");
        var lines = File.ReadAllLines(path);
        var header = lines[0].TrimStart('\ufeff').Split(',');
        var cells = lines[lineNumber - 1].Split(',');
        cells[Array.IndexOf(header, column)] = value;
        lines[lineNumber - 1] = string.Join(',', cells);
        File.WriteAllText(path, string.Join('\n', lines) + "\n", new UTF8Encoding(true));
    }
}
