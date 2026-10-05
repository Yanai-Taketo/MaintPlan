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
    public static TextTable MonthDays(IReadOnlyList<MonthDays> days, IReadOnlyList<string> columns)
    {
        if (!columns.Contains("年月"))
        {
            throw new ArgumentException("「月ごとの日数」には「年月」の列を置きます。", nameof(columns));
        }

        IReadOnlyList<(string Month, long Days)> lines = [.. days.Select(entry => (entry.Month.ToString(), (long)entry.Days)), (CellText.Total, days.Sum(entry => (long)entry.Days))];
        return GroupedTable.Build(
            "月ごとの日数",
            columns,
            lines.Select((line, index) => (line.Month, line.Days, Index: index)),
            [new("年月", line => line.Month, line => line.Index)],
            [new("日数", group => CellText.Integer(group.Sum(line => line.Days)))]);
    }

    /// <summary>
    /// 月ごとの値(例・費用内訳・年月・予算額・見積額・実績額・人工)。
    /// 見積額が空欄の費用内訳の見積額と、年割が1件もない費用内訳の予算額は「未入力」と書く。
    /// 「例」「費用内訳」の列を省いて複数の費用内訳を1行にまとめるときは、行の「例」「費用内訳」に当たる対象の費用内訳が
    /// すべて未入力のときだけ「未入力」とし、入力済みのものがあればその合計を書く。
    /// </summary>
    public static TextTable MonthlyValues(PlanData plan, MonthlyAllocationResult result, IReadOnlyList<string> columns)
    {
        var index = new PlanIndex(plan);
        var missing = MissingByRow(index, columns);
        var entries = result.Amounts
            .Select(amount => new MonthlyValueEntry(amount.CostItemId, amount.Slot, amount.Kind, amount.Amount, 0))
            .Concat(result.ManDays.Select(manDays => new MonthlyValueEntry(index.LaborLines[manDays.LaborLineId].CostItemId, manDays.Slot, null, 0, manDays.ManDaysTenths)));
        return GroupedTable.Build(
            "月ごとの値",
            columns,
            entries,
            [ExampleColumn<MonthlyValueEntry>(index, entry => entry.CostItemId), CostItemColumn<MonthlyValueEntry>(index, entry => entry.CostItemId), SlotColumn<MonthlyValueEntry>(entry => entry.Slot)],
            [
                new("予算額", group => AmountOrMissing(group, AmountKind.Budget, missing)),
                new("見積額", group => AmountOrMissing(group, AmountKind.Estimate, missing)),
                new("実績額", group => CellText.Integer(group.Where(entry => entry.Kind == AmountKind.Actual).Sum(entry => entry.Amount))),
                new("人工", group => CellText.ManDays(group.Sum(entry => entry.ManDaysTenths))),
            ]);
    }

    /// <summary>
    /// 人工の月ごとの内訳(例・費用内訳・人員区分か行・年月・人工)。
    /// 人員区分の列には、種別ごとの合計の行(「直営」など)と、区分ごとの行(「直営・機械」など)の両方を置く。
    /// </summary>
    public static TextTable MonthlyManDays(PlanData plan, MonthlyAllocationResult result, IReadOnlyList<string> columns)
    {
        var index = new PlanIndex(plan);
        var withKindTotal = columns.Contains("人員区分");
        var entries = result.ManDays.SelectMany(IEnumerable<ManDaysEntry> (manDays) =>
        {
            var line = index.LaborLines[manDays.LaborLineId];
            var category = index.StaffCategories[line.StaffCategoryId];
            var byCategory = new ManDaysEntry(line, manDays.Slot, manDays.ManDaysTenths, CellText.StaffCategory(category), StaffOrder(category, isKindTotal: false));
            return withKindTotal
                ? [byCategory with { Staff = CellText.StaffKind(category), StaffOrder = StaffOrder(category, isKindTotal: true) }, byCategory]
                : [byCategory];
        });
        return GroupedTable.Build(
            "人工の月ごとの内訳",
            columns,
            entries,
            [
                ExampleColumn<ManDaysEntry>(index, entry => entry.Line.CostItemId),
                CostItemColumn<ManDaysEntry>(index, entry => entry.Line.CostItemId),
                new("人員区分", entry => entry.Staff, entry => entry.StaffOrder),
                new("行", entry => CellText.Integer(entry.Line.Id), entry => entry.Line.Id),
                SlotColumn<ManDaysEntry>(entry => entry.Slot),
            ],
            [new("人工", group => CellText.ManDays(group.Sum(entry => entry.ManDaysTenths)))]);
    }

    /// <summary>計算に使わない修正(例・費用内訳・金額の種類・年月・金額・理由)。</summary>
    public static TextTable UnusedOverrides(PlanData plan, MonthlyAllocationResult result, IReadOnlyList<string> columns)
    {
        var index = new PlanIndex(plan);
        var entries = result.UnusedOverrides.Select(unused => (Override: index.MonthlyOverrides[unused.MonthlyOverrideId], unused.Reason));
        return GroupedTable.Build(
            "計算に使わない修正",
            columns,
            entries,
            [
                ExampleColumn<(MonthlyOverride Override, UnusedOverrideReason Reason)>(index, entry => entry.Override.CostItemId),
                CostItemColumn<(MonthlyOverride Override, UnusedOverrideReason Reason)>(index, entry => entry.Override.CostItemId),
                new("金額の種類", entry => Labels.Of(entry.Override.Kind), entry => entry.Override.Kind),
                new("年月", entry => entry.Override.Month.ToString(), entry => (entry.Override.Month.Year, entry.Override.Month.Month)),
            ],
            [
                new("金額", group => CellText.Integer(group.Sum(entry => entry.Override.Amount))),
                new("理由", group => string.Join("、", group.Select(entry => entry.Reason).Distinct().Order().Select(Labels.Of))),
            ]);
    }

    /// <summary>
    /// 予算額の初期値(例・費用内訳・労務費・予算額の初期値・年割の初期値・知らせる内容)。キーは費用内訳ID。
    /// 初期値を出さない費用内訳は、予算額の初期値を「出さない」とし、労務費と年割の初期値を空欄にする。
    /// 1行にまとめた費用内訳のうち1件でも初期値を出さないものがあれば、その行も同じように書く。
    /// 知らせる内容は、足りない単価を「足りない単価:直営・電気の2028年度」の形で「、」でつなぎ、なければ「なし」と書く。
    /// </summary>
    public static TextTable BudgetInitialValues(PlanData plan, IReadOnlyDictionary<int, BudgetInitialValueResult> results, IReadOnlyList<string> columns)
    {
        var index = new PlanIndex(plan);
        return GroupedTable.Build(
            "予算額の初期値",
            columns,
            results.Select(pair => (CostItemId: pair.Key, Result: pair.Value)),
            [ExampleColumn<(int CostItemId, BudgetInitialValueResult Result)>(index, entry => entry.CostItemId), CostItemColumn<(int CostItemId, BudgetInitialValueResult Result)>(index, entry => entry.CostItemId)],
            [
                new("労務費", group => IfProduced(group, () => CellText.Integer(group.Sum(entry => entry.Result.LaborCost!.Value)), string.Empty)),
                new("予算額の初期値", group => IfProduced(group, () => CellText.Integer(group.Sum(entry => entry.Result.Total!.Value)), CellText.NotProduced)),
                new("年割の初期値", group => IfProduced(group, () => AllocationsText(group.SelectMany(entry => entry.Result.Allocations)), string.Empty)),
                new("知らせる内容", group => NoticeText(index, group.SelectMany(entry => entry.Result.MissingUnitRates))),
            ]);
    }

    /// <summary>
    /// 労務費の内訳(例・費用内訳・人員区分か行・年度・人工・単価・労務費)。キーは費用内訳ID。
    /// 人員区分の列がある表には、種別ごとの合計の行(「直営」など)と区分ごとの行(「直営・機械」など)を置き、
    /// それぞれに年度を「合計」とした労務費の合計の行を足す。
    /// 単価は、1行にまとめた単価がすべて同じときだけ書く。初期値を出さない費用内訳の行は置かない。
    /// </summary>
    public static TextTable LaborCosts(PlanData plan, IReadOnlyDictionary<int, BudgetInitialValueResult> results, IReadOnlyList<string> columns)
    {
        if (!columns.Contains("年度"))
        {
            throw new ArgumentException("「労務費の内訳」には「年度」の列を置きます。", nameof(columns));
        }

        var byStaff = columns.Contains("人員区分");
        if (byStaff && columns.Contains("行"))
        {
            throw new ArgumentException("「労務費の内訳」には「人員区分」と「行」のどちらか一方の列を置きます。", nameof(columns));
        }

        var index = new PlanIndex(plan);
        var entries = results
            .Where(pair => pair.Value.IsProduced)
            .SelectMany(pair => pair.Value.LaborCostEntries.Select(entry => (CostItemId: pair.Key, Entry: entry)))
            .SelectMany(IEnumerable<LaborCostRow> (pair) =>
            {
                var line = index.LaborLines[pair.Entry.LaborLineId];
                var category = index.StaffCategories[line.StaffCategoryId];
                var byCategory = new LaborCostRow(pair.CostItemId, line, CellText.StaffCategory(category), StaffOrder(category, isKindTotal: false), pair.Entry.FiscalYear, pair.Entry);
                if (!byStaff)
                {
                    return [byCategory];
                }

                var byKind = byCategory with { Staff = CellText.StaffKind(category), StaffOrder = StaffOrder(category, isKindTotal: true) };
                return [byKind, byKind with { FiscalYear = null }, byCategory, byCategory with { FiscalYear = null }];
            });
        return GroupedTable.Build(
            "労務費の内訳",
            columns,
            entries,
            [
                ExampleColumn<LaborCostRow>(index, row => row.CostItemId),
                CostItemColumn<LaborCostRow>(index, row => row.CostItemId),
                new("人員区分", row => row.Staff, row => row.StaffOrder),
                new("行", row => CellText.Integer(row.Line.Id), row => row.Line.Id),
                new("年度", row => row.FiscalYear is { } fiscalYear ? CellText.FiscalYear(fiscalYear) : CellText.Total, row => row.FiscalYear ?? int.MaxValue),
            ],
            [
                new("人工", group => group[0].FiscalYear is null ? string.Empty : CellText.ManDays(group.Sum(row => row.Entry.ManDaysTenths))),
                new("単価", group => group[0].FiscalYear is null || group.Select(row => row.Entry.UnitRate).Distinct().Count() != 1
                    ? string.Empty
                    : CellText.Integer(group[0].Entry.UnitRate)),
                new("労務費", group => CellText.Integer(group.Sum(row => row.Entry.Cost))),
            ]);
    }

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

    private static KeyColumn<TEntry> ExampleColumn<TEntry>(PlanIndex index, Func<TEntry, int> costItemId) =>
        new("例", entry => index.WorkOf(costItemId(entry)).ManagementNumber, entry => index.WorkOf(costItemId(entry)).Id);

    private static KeyColumn<TEntry> CostItemColumn<TEntry>(PlanIndex index, Func<TEntry, int> costItemId) =>
        new("費用内訳", entry => Labels.Of(index.CostItems[costItemId(entry)].Category), entry => index.CostItems[costItemId(entry)].Category);

    private static KeyColumn<TEntry> SlotColumn<TEntry>(Func<TEntry, Slot> slot) =>
        new("年月", entry => CellText.Slot(slot(entry)), entry => CellText.SlotOrder(slot(entry)));

    private static IComparable StaffOrder(StaffCategory category, bool isKindTotal) =>
        (category.Kind, !isKindTotal, isKindTotal ? 0 : category.DisplayOrder, isKindTotal ? 0 : category.Id);

    /// <summary>予算額か見積額のセル。行がすべて未入力なら「未入力」、そうでなければ合計。</summary>
    private static string AmountOrMissing(IReadOnlyList<MonthlyValueEntry> group, AmountKind kind, Func<int, AmountKind, bool> isRowMissing) =>
        isRowMissing(group[0].CostItemId, kind)
            ? CellText.Missing
            : CellText.Integer(group.Where(entry => entry.Kind == kind).Sum(entry => entry.Amount));

    /// <summary>
    /// 費用内訳 ID と金額の種類から、その費用内訳が入る行が未入力かを返す関数を作る。
    /// 行の「例」「費用内訳」に当たる対象の費用内訳がすべて未入力なら未入力とする。列を省いた「例」「費用内訳」では絞らない。
    /// </summary>
    private static Func<int, AmountKind, bool> MissingByRow(PlanIndex index, IReadOnlyList<string> columns)
    {
        var byExample = columns.Contains("例");
        var byCategory = columns.Contains("費用内訳");
        (int? WorkId, CostCategory? Category) RowOf(CostItem item) =>
            (byExample ? item.ConstructionWorkId : null, byCategory ? item.Category : null);

        var rows = index.TargetCostItems
            .GroupBy(RowOf)
            .ToDictionary(
                group => group.Key,
                group => new HashSet<AmountKind>(
                    new[] { AmountKind.Budget, AmountKind.Estimate }.Where(kind => group.All(item => index.IsMissing(item.Id, kind)))));
        return (costItemId, kind) => rows[RowOf(index.CostItems[costItemId])].Contains(kind);
    }

    /// <summary>行のすべての費用内訳が初期値を出していれば produced を、そうでなければ notProduced を返す。</summary>
    private static string IfProduced(IReadOnlyList<(int CostItemId, BudgetInitialValueResult Result)> group, Func<string> produced, string notProduced) =>
        group.All(entry => entry.Result.IsProduced) ? produced() : notProduced;

    /// <summary>年割の初期値。年度ごとに合計し、「2026年度 1603076、2027年度 1731324」の形で書く。</summary>
    private static string AllocationsText(IEnumerable<FiscalYearAmount> allocations) =>
        string.Join("、", allocations
            .GroupBy(allocation => allocation.FiscalYear)
            .OrderBy(group => group.Key)
            .Select(group => $"{CellText.FiscalYear(group.Key)} {CellText.Integer(group.Sum(allocation => allocation.Amount))}"));

    /// <summary>知らせる内容。足りない単価を種別・表示順・年度の順に並べる。なければ「なし」。</summary>
    private static string NoticeText(PlanIndex index, IEnumerable<MissingUnitRate> missing)
    {
        var items = BudgetInitialValue.OrderMissingUnitRates(missing, index.StaffCategories.Values)
            .Select(rate => $"{CellText.StaffCategory(index.StaffCategories[rate.StaffCategoryId])}の{CellText.FiscalYear(rate.FiscalYear)}")
            .ToList();
        return items.Count == 0 ? CellText.None : $"足りない単価:{string.Join("、", items)}";
    }

    /// <summary>月ごとの値の表の項目。金額(Kind が金額の種類)か人工(Kind が null)のどちらか。</summary>
    private sealed record MonthlyValueEntry(int CostItemId, Slot Slot, AmountKind? Kind, long Amount, long ManDaysTenths);

    /// <summary>人工の月ごとの内訳の表の項目。Staff は人員区分の列に書く文字。</summary>
    private sealed record ManDaysEntry(LaborLine Line, Slot Slot, long ManDaysTenths, string Staff, IComparable StaffOrder);

    /// <summary>労務費の内訳の表の項目。Staff は人員区分の列に書く文字。FiscalYear が null なら合計の行。</summary>
    private sealed record LaborCostRow(int CostItemId, LaborLine Line, string Staff, IComparable StaffOrder, int? FiscalYear, LaborCostEntry Entry);
}
