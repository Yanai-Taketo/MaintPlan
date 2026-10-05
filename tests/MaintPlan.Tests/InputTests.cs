using MaintPlan.Core.Model;
using MaintPlan.IO.Csv;
using MaintPlan.Tests.Support;

namespace MaintPlan.Tests;

/// <summary>各ケースの入力を読み込み、テーブルどうしのつながりと3章の決まりを確かめる。</summary>
public class InputTests
{
    [Theory]
    [MemberData(nameof(TestCases.Ids), MemberType = typeof(TestCases))]
    public void Input_can_be_read_and_is_consistent(string caseId)
    {
        var plan = PlanCsvReader.ReadFolder(TestCases.Get(caseId).InputDirectory);
        var problems = new List<string>();

        void Unique<T, TKey>(string table, IEnumerable<T> rows, Func<T, TKey> key, string what)
        {
            foreach (var group in rows.GroupBy(key).Where(group => group.Count() > 1))
            {
                problems.Add($"{table}: {what}が同じ行が{group.Count()}件あります({group.Key})。");
            }
        }

        void Exists(string table, int id, string column, IEnumerable<int> ids)
        {
            if (!ids.Contains(id))
            {
                problems.Add($"{table}: {column} {id} に当たる行がありません。");
            }
        }

        void BothOrNeither(string table, int id, DateOnly? start, DateOnly? end)
        {
            if (start.HasValue != end.HasValue)
            {
                problems.Add($"{table}: ID {id} の開始日と終了日は、両方入れるか両方空欄にします。");
            }
        }

        Unique("工事", plan.ConstructionWorks, row => row.Id, "ID");
        Unique("費用内訳", plan.CostItems, row => row.Id, "ID");
        Unique("予算年割", plan.AnnualBudgets, row => row.Id, "ID");
        Unique("実績", plan.ActualCosts, row => row.Id, "ID");
        Unique("月別修正", plan.MonthlyOverrides, row => row.Id, "ID");
        Unique("作業明細", plan.LaborLines, row => row.Id, "ID");
        Unique("人員区分", plan.StaffCategories, row => row.Id, "ID");
        Unique("単価", plan.UnitRates, row => row.Id, "ID");
        Unique("予算枠", plan.BudgetFrames, row => row.Id, "ID");

        Unique("工事", plan.ConstructionWorks, row => row.ManagementNumber, "管理番号");
        Unique("費用内訳", plan.CostItems, row => (row.ConstructionWorkId, row.Category), "工事と費用区分");
        Unique("費用内訳", plan.CostItems.Where(row => row.WorkNumber is not null), row => row.WorkNumber, "工事番号");
        Unique("予算年割", plan.AnnualBudgets, row => (row.CostItemId, row.FiscalYear), "費用内訳と年度");
        Unique("月別修正", plan.MonthlyOverrides, row => (row.CostItemId, row.Kind, row.Month), "費用内訳・金額の種類・年月");
        Unique("人員区分", plan.StaffCategories, row => (row.Kind, row.Name), "種別と区分名");
        Unique("単価", plan.UnitRates, row => (row.StaffCategoryId, row.FiscalYear), "人員区分と年度");
        Unique("予算枠", plan.BudgetFrames, row => (row.FiscalYear, row.Category), "年度と費用区分");

        var works = plan.ConstructionWorks.Select(row => row.Id).ToHashSet();
        var costItems = plan.CostItems.Select(row => row.Id).ToHashSet();
        var staff = plan.StaffCategories.Select(row => row.Id).ToHashSet();
        plan.CostItems.ToList().ForEach(row => Exists("費用内訳", row.ConstructionWorkId, "工事ID", works));
        plan.AnnualBudgets.ToList().ForEach(row => Exists("予算年割", row.CostItemId, "費用内訳ID", costItems));
        plan.ActualCosts.ToList().ForEach(row => Exists("実績", row.CostItemId, "費用内訳ID", costItems));
        plan.MonthlyOverrides.ToList().ForEach(row => Exists("月別修正", row.CostItemId, "費用内訳ID", costItems));
        plan.LaborLines.ToList().ForEach(row => Exists("作業明細", row.CostItemId, "費用内訳ID", costItems));
        plan.LaborLines.ToList().ForEach(row => Exists("作業明細", row.StaffCategoryId, "人員区分ID", staff));
        plan.UnitRates.ToList().ForEach(row => Exists("単価", row.StaffCategoryId, "人員区分ID", staff));

        plan.ConstructionWorks.ToList().ForEach(row => BothOrNeither("工事", row.Id, row.StartDate, row.EndDate));
        plan.LaborLines.ToList().ForEach(row => BothOrNeither("作業明細", row.Id, row.StartDate, row.EndDate));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
