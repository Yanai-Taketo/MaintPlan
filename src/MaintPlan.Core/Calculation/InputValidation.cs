using System.Globalization;
using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>
/// 入力の確認。設計書3章の決まり(重複なし、開始日と終了日は両方入れるか両方空欄)、参照先の行があること、終了日が開始日より前でないこと、
/// 月別修正の金額の種類と、4章の保存できない条件を確かめ、当たる行をすべて返す。
/// ID・管理番号の重複と参照先は、削除済みの行も含めて確かめる。参照先の行は、削除済みでもよい。そのほかは、削除済みの行を確かめない。
/// 保存できない条件は、保存の確認が例外になる行と、どの行を指すかが決まらない行を除いて確かめる。
/// </summary>
public static class InputValidation
{
    private static readonly string[] DateColumns = ["開始日", "終了日"];

    public static InputValidationResult Validate(PlanData plan)
    {
        List<InputViolation> violations =
        [
            .. DuplicateIds(plan),
            .. DuplicateValues(plan),
            .. DateViolations(plan),
            .. MissingReferences(plan),
            .. OverrideKindViolations(plan),
        ];
        violations.AddRange([.. SaveViolations(plan, violations)]);
        return new InputValidationResult([.. violations.OrderBy(violation => violation.Table).ThenBy(violation => violation.RowIndex)]);
    }

    private static IEnumerable<InputViolation> DuplicateIds(PlanData plan) =>
    [
        .. DuplicateIdsOf(PlanTable.ConstructionWork, plan.ConstructionWorks, row => row.Id),
        .. DuplicateIdsOf(PlanTable.CostItem, plan.CostItems, row => row.Id),
        .. DuplicateIdsOf(PlanTable.AnnualBudget, plan.AnnualBudgets, row => row.Id),
        .. DuplicateIdsOf(PlanTable.ActualCost, plan.ActualCosts, row => row.Id),
        .. DuplicateIdsOf(PlanTable.MonthlyOverride, plan.MonthlyOverrides, row => row.Id),
        .. DuplicateIdsOf(PlanTable.LaborLine, plan.LaborLines, row => row.Id),
        .. DuplicateIdsOf(PlanTable.StaffCategory, plan.StaffCategories, row => row.Id),
        .. DuplicateIdsOf(PlanTable.UnitRate, plan.UnitRates, row => row.Id),
        .. DuplicateIdsOf(PlanTable.BudgetFrame, plan.BudgetFrames, row => row.Id),
    ];

    private static IEnumerable<InputViolation> DuplicateIdsOf<T>(PlanTable table, IReadOnlyList<T> rows, Func<T, int> id) =>
        Duplicates(table, rows, _ => true, id, InputViolationKind.DuplicateId, ["ID"], row => $"ID {Number(id(row))} ");

    /// <summary>3章の「重複なし」。管理番号は削除済みの行も含め、そのほかは削除済みでない行どうしで確かめる。工事番号は、入れた行だけを確かめる。</summary>
    private static IEnumerable<InputViolation> DuplicateValues(PlanData plan) =>
    [
        .. Duplicates(PlanTable.ConstructionWork, plan.ConstructionWorks, _ => true, row => row.ManagementNumber,
            InputViolationKind.DuplicateValue, ["管理番号"], row => $"管理番号{Text(row.ManagementNumber)}"),
        .. Duplicates(PlanTable.CostItem, plan.CostItems, row => !row.IsDeleted, row => (row.ConstructionWorkId, row.Category),
            InputViolationKind.DuplicateValue, ["工事ID", "費用区分"], row => $"工事ID {Number(row.ConstructionWorkId)} と費用区分{Text(Labels.Of(row.Category))}の組み合わせ"),
        .. Duplicates(PlanTable.CostItem, plan.CostItems, row => !row.IsDeleted && row.WorkNumber is not null, row => row.WorkNumber!,
            InputViolationKind.DuplicateValue, ["工事番号"], row => $"工事番号{Text(row.WorkNumber!)}"),
        .. Duplicates(PlanTable.AnnualBudget, plan.AnnualBudgets, row => !row.IsDeleted, row => (row.CostItemId, row.FiscalYear),
            InputViolationKind.DuplicateValue, ["費用内訳ID", "年度"], row => $"費用内訳ID {Number(row.CostItemId)} と年度 {Number(row.FiscalYear)} の組み合わせ"),
        .. Duplicates(PlanTable.MonthlyOverride, plan.MonthlyOverrides, row => !row.IsDeleted, row => (row.CostItemId, row.Kind, row.Month),
            InputViolationKind.DuplicateValue, ["費用内訳ID", "金額の種類", "年月"],
            row => $"費用内訳ID {Number(row.CostItemId)}・金額の種類{Text(Labels.Of(row.Kind))}・年月 {row.Month} の組み合わせ"),
        .. Duplicates(PlanTable.StaffCategory, plan.StaffCategories, row => !row.IsDeleted, row => (row.Kind, row.Name),
            InputViolationKind.DuplicateValue, ["種別", "区分名"], row => $"種別{Text(Labels.Of(row.Kind))}と区分名{Text(row.Name)}の組み合わせ"),
        .. Duplicates(PlanTable.UnitRate, plan.UnitRates, row => !row.IsDeleted, row => (row.StaffCategoryId, row.FiscalYear),
            InputViolationKind.DuplicateValue, ["人員区分ID", "年度"], row => $"人員区分ID {Number(row.StaffCategoryId)} と年度 {Number(row.FiscalYear)} の組み合わせ"),
        .. Duplicates(PlanTable.BudgetFrame, plan.BudgetFrames, row => !row.IsDeleted, row => (row.FiscalYear, row.Category),
            InputViolationKind.DuplicateValue, ["年度", "費用区分"], row => $"年度 {Number(row.FiscalYear)} と費用区分{Text(Labels.Of(row.Category))}の組み合わせ"),
    ];

    /// <summary>include に合う行のうち、key が前の行と同じ行を、最初の行の位置とともに返す。subject は理由の文の主語。</summary>
    private static IEnumerable<InputViolation> Duplicates<T, TKey>(
        PlanTable table,
        IReadOnlyList<T> rows,
        Func<T, bool> include,
        Func<T, TKey> key,
        InputViolationKind kind,
        IReadOnlyList<string> columns,
        Func<T, string> subject)
        where TKey : notnull
    {
        var first = new Dictionary<TKey, int>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (include(row) && !first.TryAdd(key(row), index))
            {
                yield return new InputViolation(kind, table, index, columns, $"{subject(row)}が重複しています。", first[key(row)]);
            }
        }
    }

    /// <summary>工事と作業明細の開始日と終了日。削除済みでない行を確かめる。</summary>
    private static IEnumerable<InputViolation> DateViolations(PlanData plan) =>
    [
        .. DateViolationsOf(PlanTable.ConstructionWork, plan.ConstructionWorks, row => row.IsDeleted, row => (row.StartDate, row.EndDate)),
        .. DateViolationsOf(PlanTable.LaborLine, plan.LaborLines, row => row.IsDeleted, row => (row.StartDate, row.EndDate)),
    ];

    private static IEnumerable<InputViolation> DateViolationsOf<T>(
        PlanTable table, IReadOnlyList<T> rows, Func<T, bool> isDeleted, Func<T, (DateOnly? Start, DateOnly? End)> datesOf)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (isDeleted(rows[index]))
            {
                continue;
            }

            var (start, end) = datesOf(rows[index]);
            if (start.HasValue != end.HasValue)
            {
                yield return new InputViolation(InputViolationKind.DatesNotPaired, table, index, DateColumns, "開始日と終了日は、両方入れるか両方空欄にします。");
            }
            else if (start is { } s && end is { } e && e < s)
            {
                yield return new InputViolation(InputViolationKind.EndBeforeStart, table, index, DateColumns, $"終了日 {Date(e)} が開始日 {Date(s)} より前です。");
            }
        }
    }

    /// <summary>参照先の行。削除済みの行も確かめ、参照先は削除済みの行でもよいとする。</summary>
    private static IEnumerable<InputViolation> MissingReferences(PlanData plan)
    {
        var works = plan.ConstructionWorks.Select(row => row.Id).ToHashSet();
        var costItems = plan.CostItems.Select(row => row.Id).ToHashSet();
        var staffCategories = plan.StaffCategories.Select(row => row.Id).ToHashSet();
        return
        [
            .. MissingReferencesOf(PlanTable.CostItem, plan.CostItems, row => row.ConstructionWorkId, "工事ID", PlanTable.ConstructionWork, works),
            .. MissingReferencesOf(PlanTable.AnnualBudget, plan.AnnualBudgets, row => row.CostItemId, "費用内訳ID", PlanTable.CostItem, costItems),
            .. MissingReferencesOf(PlanTable.ActualCost, plan.ActualCosts, row => row.CostItemId, "費用内訳ID", PlanTable.CostItem, costItems),
            .. MissingReferencesOf(PlanTable.MonthlyOverride, plan.MonthlyOverrides, row => row.CostItemId, "費用内訳ID", PlanTable.CostItem, costItems),
            .. MissingReferencesOf(PlanTable.LaborLine, plan.LaborLines, row => row.CostItemId, "費用内訳ID", PlanTable.CostItem, costItems),
            .. MissingReferencesOf(PlanTable.LaborLine, plan.LaborLines, row => row.StaffCategoryId, "人員区分ID", PlanTable.StaffCategory, staffCategories),
            .. MissingReferencesOf(PlanTable.UnitRate, plan.UnitRates, row => row.StaffCategoryId, "人員区分ID", PlanTable.StaffCategory, staffCategories),
        ];
    }

    private static IEnumerable<InputViolation> MissingReferencesOf<T>(
        PlanTable table, IReadOnlyList<T> rows, Func<T, int> reference, string column, PlanTable referenced, IReadOnlySet<int> ids)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var id = reference(rows[index]);
            if (!ids.Contains(id))
            {
                yield return new InputViolation(
                    InputViolationKind.MissingReference, table, index, [column], $"{column} {Number(id)} に当たる{Labels.Of(referenced)}の行がありません。");
            }
        }
    }

    /// <summary>月別修正の金額の種類。削除済みでない行を確かめる。</summary>
    private static IEnumerable<InputViolation> OverrideKindViolations(PlanData plan)
    {
        for (var index = 0; index < plan.MonthlyOverrides.Count; index++)
        {
            var row = plan.MonthlyOverrides[index];
            if (!row.IsDeleted && row.Kind is not (AmountKind.Budget or AmountKind.Estimate))
            {
                yield return new InputViolation(InputViolationKind.InvalidOverrideKind, PlanTable.MonthlyOverride, index, ["金額の種類"], "月別修正の金額の種類は、予算額か見積額です。");
            }
        }
    }

    /// <summary>
    /// 保存できない条件。保存の確認が例外になる行と、どの行を指すかが決まらない行を除いた入力で確かめる。
    /// 除くのは、工事・費用内訳・作業明細の ID が重複した行(最初の行も)、工事と作業明細の日付が決まりに合わない行、
    /// 費用内訳と年度が重複した年割の行(最初の行も)、金額の種類が決まりに合わない月別修正の行と、
    /// 組み合わせが重複した月別修正の、費用内訳・金額の種類が同じ修正すべて(修正の合計が決まらないため)。
    /// 作業明細の理由は作業明細の行、見積額の理由は費用内訳の行、予算額の理由はその年度の年割の行で示す。
    /// </summary>
    private static IEnumerable<InputViolation> SaveViolations(PlanData plan, IReadOnlyList<InputViolation> found)
    {
        var excludedRows = new HashSet<(PlanTable Table, int RowIndex)>();
        var excludedOverrides = new HashSet<(int CostItemId, AmountKind Kind)>();
        foreach (var violation in found)
        {
            switch (violation.Kind, violation.Table)
            {
                case (InputViolationKind.DuplicateId, PlanTable.ConstructionWork or PlanTable.CostItem or PlanTable.LaborLine):
                case (InputViolationKind.DuplicateValue, PlanTable.AnnualBudget):
                    excludedRows.Add((violation.Table, violation.RowIndex));
                    excludedRows.Add((violation.Table, violation.FirstRowIndex!.Value));
                    break;
                case (InputViolationKind.DatesNotPaired or InputViolationKind.EndBeforeStart or InputViolationKind.InvalidOverrideKind, _):
                    excludedRows.Add((violation.Table, violation.RowIndex));
                    break;
                case (InputViolationKind.DuplicateValue, PlanTable.MonthlyOverride):
                {
                    var duplicated = plan.MonthlyOverrides[violation.RowIndex];
                    excludedOverrides.Add((duplicated.CostItemId, duplicated.Kind));
                    break;
                }
            }
        }

        IReadOnlyList<T> Keep<T>(PlanTable table, IReadOnlyList<T> rows) => [.. rows.Where((_, index) => !excludedRows.Contains((table, index)))];

        var checkedPlan = plan with
        {
            ConstructionWorks = Keep(PlanTable.ConstructionWork, plan.ConstructionWorks),
            CostItems = Keep(PlanTable.CostItem, plan.CostItems),
            AnnualBudgets = Keep(PlanTable.AnnualBudget, plan.AnnualBudgets),
            MonthlyOverrides = [.. Keep(PlanTable.MonthlyOverride, plan.MonthlyOverrides).Where(o => !excludedOverrides.Contains((o.CostItemId, o.Kind)))],
            LaborLines = Keep(PlanTable.LaborLine, plan.LaborLines),
        };

        int IndexOf<T>(PlanTable table, IReadOnlyList<T> rows, Func<T, bool> match) =>
            Enumerable.Range(0, rows.Count).First(index => !excludedRows.Contains((table, index)) && match(rows[index]));

        foreach (var violation in SaveValidation.Validate(checkedPlan).Violations)
        {
            var itemIndex = IndexOf(PlanTable.CostItem, plan.CostItems, row => row.Id == violation.CostItemId);
            var item = plan.CostItems[itemIndex];
            if (violation.LaborLineId is { } laborLineId)
            {
                var index = IndexOf(PlanTable.LaborLine, plan.LaborLines, row => row.Id == laborLineId);
                var work = plan.ConstructionWorks[IndexOf(PlanTable.ConstructionWork, plan.ConstructionWorks, row => row.Id == item.ConstructionWorkId)];
                yield return new InputViolation(
                    InputViolationKind.CannotSave, PlanTable.LaborLine, index, DateColumns, LaborLineMessage(violation, plan.LaborLines[index], work), SaveViolation: violation);
            }
            else if (violation.FiscalYear is { } fiscalYear)
            {
                var index = IndexOf(PlanTable.AnnualBudget, plan.AnnualBudgets, row => !row.IsDeleted && row.CostItemId == item.Id && row.FiscalYear == fiscalYear);
                yield return new InputViolation(InputViolationKind.CannotSave, PlanTable.AnnualBudget, index, ["予算額"], OverrideMessage(violation), SaveViolation: violation);
            }
            else
            {
                yield return new InputViolation(InputViolationKind.CannotSave, PlanTable.CostItem, itemIndex, ["見積額"], OverrideMessage(violation), SaveViolation: violation);
            }
        }
    }

    private static string LaborLineMessage(SaveViolation violation, LaborLine line, ConstructionWork work) => violation.Kind switch
    {
        SaveViolationKind.LaborLineOutsidePeriod =>
            $"作業明細の期間 {Date(line.StartDate!.Value)}〜{Date(line.EndDate!.Value)} が、工期 {Date(work.StartDate!.Value)}〜{Date(work.EndDate!.Value)} の外にあります。",
        SaveViolationKind.LaborLineDatedWithoutWorkDates => "工事の日付が空欄で、作業明細に日付があります。",
        _ => throw new ArgumentOutOfRangeException(nameof(violation)),
    };

    private static string OverrideMessage(SaveViolation violation)
    {
        var total = Amount(violation.OverrideTotal!.Value);
        var compared = Amount(violation.ComparedAmount!.Value);
        var fiscalYear = violation.FiscalYear is { } year ? $"{Number(year)}年度の" : string.Empty;
        return violation.Kind switch
        {
            SaveViolationKind.EstimateOverridesExceed => $"見積額の修正の合計 {total} が、見積額 {compared} を超えています。",
            SaveViolationKind.EstimateOverridesMismatch => $"見積額の全部の月を修正していて、修正の合計 {total} が見積額 {compared} と一致しません。",
            SaveViolationKind.BudgetOverridesExceed => $"{fiscalYear}予算額の修正の合計 {total} が、年割額 {compared} を超えています。",
            SaveViolationKind.BudgetOverridesMismatch => $"{fiscalYear}予算額の全部の月を修正していて、修正の合計 {total} が年割額 {compared} と一致しません。",
            _ => throw new ArgumentOutOfRangeException(nameof(violation)),
        };
    }

    private static string Text(string value) => $"「{value}」";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Amount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
