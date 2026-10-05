using MaintPlan.Core.Model;

namespace MaintPlan.IO.Csv;

/// <summary>
/// 計算に使う9つのテーブルを、テーブルごとの CSV ファイル(「工事.csv」など)から読む。
/// 列名は設計書3章の項目名と「削除済み」とし、過不足があれば読まない。
/// </summary>
public static class PlanCsvReader
{
    public const string Deleted = "削除済み";

    /// <summary>テーブル名と、その CSV の列名。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> TableColumns { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["工事"] = ["ID", "管理番号", "工事名", "状態", "開始日", "終了日", "実施年度", "設備名", "設置場所", "担当者", "担当部署", "協力会社名", "優先度", "備考", Deleted],
            ["費用内訳"] = ["ID", "工事ID", "費用区分", "計上の考え方", "工事番号", "見積額", "その他の費用", "その他の費用の内容", Deleted],
            ["予算年割"] = ["ID", "費用内訳ID", "年度", "予算額", Deleted],
            ["実績"] = ["ID", "費用内訳ID", "年月", "実績額", "摘要", Deleted],
            ["月別修正"] = ["ID", "費用内訳ID", "金額の種類", "年月", "金額", Deleted],
            ["作業明細"] = ["ID", "費用内訳ID", "人員区分ID", "内容", "開始日", "終了日", "人数", "作業日数", Deleted],
            ["人員区分"] = ["ID", "種別", "区分名", "予算額の計算に含める", "表示順", "使用中", Deleted],
            ["単価"] = ["ID", "人員区分ID", "年度", "単価", Deleted],
            ["予算枠"] = ["ID", "年度", "費用区分", "予算枠額", Deleted],
        };

    /// <summary>フォルダにある9つの CSV を読む。</summary>
    public static PlanData ReadFolder(string folderPath) => new()
    {
        ConstructionWorks = Read(folderPath, "工事", row => new ConstructionWork
        {
            Id = row.Int("ID"),
            ManagementNumber = row.Text("管理番号"),
            Name = row.Text("工事名"),
            Status = row.Choice<WorkStatus>("状態"),
            StartDate = row.OptionalDate("開始日"),
            EndDate = row.OptionalDate("終了日"),
            PlannedFiscalYear = row.OptionalFiscalYear("実施年度"),
            EquipmentName = row.OptionalText("設備名"),
            Location = row.OptionalText("設置場所"),
            PersonInCharge = row.OptionalText("担当者"),
            Department = row.OptionalText("担当部署"),
            ContractorName = row.OptionalText("協力会社名"),
            Priority = row.OptionalChoice<Priority>("優先度"),
            Remarks = row.OptionalText("備考"),
            IsDeleted = row.Bool(Deleted),
        }),
        CostItems = Read(folderPath, "費用内訳", row => new CostItem
        {
            Id = row.Int("ID"),
            ConstructionWorkId = row.Int("工事ID"),
            Category = row.Choice<CostCategory>("費用区分"),
            Recognition = row.Choice<RecognitionMethod>("計上の考え方"),
            WorkNumber = row.OptionalText("工事番号"),
            EstimateAmount = row.OptionalAmount("見積額"),
            OtherCost = row.OptionalAmount("その他の費用"),
            OtherCostDescription = row.OptionalText("その他の費用の内容"),
            IsDeleted = row.Bool(Deleted),
        }),
        AnnualBudgets = Read(folderPath, "予算年割", row => new AnnualBudget
        {
            Id = row.Int("ID"),
            CostItemId = row.Int("費用内訳ID"),
            FiscalYear = row.FiscalYear("年度"),
            Amount = row.Amount("予算額"),
            IsDeleted = row.Bool(Deleted),
        }),
        ActualCosts = Read(folderPath, "実績", row => new ActualCost
        {
            Id = row.Int("ID"),
            CostItemId = row.Int("費用内訳ID"),
            Month = row.Month("年月"),
            Amount = row.Amount("実績額"),
            Note = row.OptionalText("摘要"),
            IsDeleted = row.Bool(Deleted),
        }),
        MonthlyOverrides = Read(folderPath, "月別修正", row => new MonthlyOverride
        {
            Id = row.Int("ID"),
            CostItemId = row.Int("費用内訳ID"),
            Kind = row.Choice<AmountKind>("金額の種類") is var kind && kind != AmountKind.Actual
                ? kind
                : throw row.Error("金額の種類", "月別修正の金額の種類は、予算額か見積額です。"),
            Month = row.Month("年月"),
            Amount = row.Amount("金額"),
            IsDeleted = row.Bool(Deleted),
        }),
        LaborLines = Read(folderPath, "作業明細", row => new LaborLine
        {
            Id = row.Int("ID"),
            CostItemId = row.Int("費用内訳ID"),
            StaffCategoryId = row.Int("人員区分ID"),
            Description = row.OptionalText("内容"),
            StartDate = row.OptionalDate("開始日"),
            EndDate = row.OptionalDate("終了日"),
            Headcount = row.Int("人数"),
            WorkDaysTenths = row.Tenths("作業日数"),
            IsDeleted = row.Bool(Deleted),
        }),
        StaffCategories = Read(folderPath, "人員区分", row => new StaffCategory
        {
            Id = row.Int("ID"),
            Kind = row.Choice<StaffKind>("種別"),
            Name = row.Text("区分名"),
            IncludeInBudget = row.Bool("予算額の計算に含める"),
            DisplayOrder = row.Int("表示順"),
            InUse = row.Bool("使用中"),
            IsDeleted = row.Bool(Deleted),
        }),
        UnitRates = Read(folderPath, "単価", row => new UnitRate
        {
            Id = row.Int("ID"),
            StaffCategoryId = row.Int("人員区分ID"),
            FiscalYear = row.FiscalYear("年度"),
            Rate = row.Amount("単価"),
            IsDeleted = row.Bool(Deleted),
        }),
        BudgetFrames = Read(folderPath, "予算枠", row => new BudgetFrame
        {
            Id = row.Int("ID"),
            FiscalYear = row.FiscalYear("年度"),
            Category = row.Choice<CostCategory>("費用区分"),
            Amount = row.Amount("予算枠額"),
            IsDeleted = row.Bool(Deleted),
        }),
    };

    private static List<T> Read<T>(string folderPath, string tableName, Func<CsvRowReader, T> map)
    {
        var filePath = Path.Combine(folderPath, tableName + ".csv");
        if (!File.Exists(filePath))
        {
            throw new CsvFormatException(filePath, null, null, "ファイルがありません。");
        }

        var table = CsvTable.Read(filePath);
        var expected = TableColumns[tableName];
        var missing = expected.Except(table.Columns).ToList();
        var unknown = table.Columns.Except(expected).ToList();
        if (missing.Count > 0 || unknown.Count > 0)
        {
            var problems = new List<string>();
            if (missing.Count > 0)
            {
                problems.Add($"足りない列: {string.Join("、", missing)}");
            }

            if (unknown.Count > 0)
            {
                problems.Add($"知らない列: {string.Join("、", unknown)}");
            }

            throw new CsvFormatException(filePath, 1, null, string.Join("。", problems));
        }

        return [.. table.Rows.Select(row => map(new CsvRowReader(table, row)))];
    }
}
