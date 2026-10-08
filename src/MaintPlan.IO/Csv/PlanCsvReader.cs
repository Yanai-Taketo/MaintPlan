using MaintPlan.Core.Model;
using MaintPlan.IO.Input;

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

    /// <summary>
    /// フォルダにある9つの CSV を読む。形の違う値は、最初の1件で止めずにすべて集める。
    /// 形の違う値がなければ、読んだ内容と、行のファイルでの場所を返す。
    /// </summary>
    public static PlanReadResult Read(string folderPath)
    {
        var reader = new FolderReader(folderPath);
        var plan = new PlanData
        {
            ConstructionWorks = reader.Read(PlanTable.ConstructionWork, row => new ConstructionWork
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
            CostItems = reader.Read(PlanTable.CostItem, row => new CostItem
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
            AnnualBudgets = reader.Read(PlanTable.AnnualBudget, row => new AnnualBudget
            {
                Id = row.Int("ID"),
                CostItemId = row.Int("費用内訳ID"),
                FiscalYear = row.FiscalYear("年度"),
                Amount = row.Amount("予算額"),
                IsDeleted = row.Bool(Deleted),
            }),
            ActualCosts = reader.Read(PlanTable.ActualCost, row => new ActualCost
            {
                Id = row.Int("ID"),
                CostItemId = row.Int("費用内訳ID"),
                Month = row.Month("年月"),
                Amount = row.Amount("実績額"),
                Note = row.OptionalText("摘要"),
                IsDeleted = row.Bool(Deleted),
            }),
            MonthlyOverrides = reader.Read(PlanTable.MonthlyOverride, row => new MonthlyOverride
            {
                Id = row.Int("ID"),
                CostItemId = row.Int("費用内訳ID"),
                Kind = OverrideKind(row),
                Month = row.Month("年月"),
                Amount = row.Amount("金額"),
                IsDeleted = row.Bool(Deleted),
            }),
            LaborLines = reader.Read(PlanTable.LaborLine, row => new LaborLine
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
            StaffCategories = reader.Read(PlanTable.StaffCategory, row => new StaffCategory
            {
                Id = row.Int("ID"),
                Kind = row.Choice<StaffKind>("種別"),
                Name = row.Text("区分名"),
                IncludeInBudget = row.Bool("予算額の計算に含める"),
                DisplayOrder = row.Int("表示順"),
                InUse = row.Bool("使用中"),
                IsDeleted = row.Bool(Deleted),
            }),
            UnitRates = reader.Read(PlanTable.UnitRate, row => new UnitRate
            {
                Id = row.Int("ID"),
                StaffCategoryId = row.Int("人員区分ID"),
                FiscalYear = row.FiscalYear("年度"),
                Rate = row.Amount("単価"),
                IsDeleted = row.Bool(Deleted),
            }),
            BudgetFrames = reader.Read(PlanTable.BudgetFrame, row => new BudgetFrame
            {
                Id = row.Int("ID"),
                FiscalYear = row.FiscalYear("年度"),
                Category = row.Choice<CostCategory>("費用区分"),
                Amount = row.Amount("予算枠額"),
                IsDeleted = row.Bool(Deleted),
            }),
        };
        return reader.Errors.Count > 0
            ? new PlanReadResult(null, null, reader.Errors)
            : new PlanReadResult(plan, new PlanSource(reader.Sources), []);
    }

    /// <summary>フォルダにある9つの CSV を読む。形の違う値があれば、最初の1件を投げる。</summary>
    public static PlanData ReadFolder(string folderPath)
    {
        var result = Read(folderPath);
        return result.Plan ?? throw result.Errors[0];
    }

    /// <summary>月別修正の金額の種類。実績額は形の違う値とする。</summary>
    private static AmountKind OverrideKind(CsvRowReader row)
    {
        var kind = row.Choice<AmountKind>("金額の種類");
        if (kind == AmountKind.Actual)
        {
            row.Report("金額の種類", "月別修正の金額の種類は、予算額か見積額です。");
        }

        return kind;
    }

    /// <summary>1つのフォルダの CSV を、テーブルごとに読む。形の違う値と、行のファイルでの場所を集める。</summary>
    private sealed class FolderReader(string folderPath)
    {
        public List<CsvFormatException> Errors { get; } = [];

        public Dictionary<PlanTable, TableSource> Sources { get; } = [];

        /// <summary>テーブルの CSV を読む。ファイルや列名に誤りがあれば、そのテーブルの行は読まない。</summary>
        public List<T> Read<T>(PlanTable table, Func<CsvRowReader, T> map)
        {
            var tableName = Labels.Of(table);
            var filePath = Path.Combine(folderPath, tableName + ".csv");
            if (!File.Exists(filePath))
            {
                Errors.Add(new CsvFormatException(filePath, null, null, "ファイルがありません。"));
                return [];
            }

            CsvTable csvTable;
            try
            {
                csvTable = CsvTable.Read(filePath);
            }
            catch (CsvFormatException exception)
            {
                Errors.Add(exception);
                return [];
            }

            var expected = TableColumns[tableName];
            var missing = expected.Except(csvTable.Columns).ToList();
            var unknown = csvTable.Columns.Except(expected).ToList();
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

                Errors.Add(new CsvFormatException(filePath, 1, null, string.Join("。", problems)));
                return [];
            }

            Sources[table] = new TableSource(filePath, [.. csvTable.Rows.Select(row => row.LineNumber)]);
            return [.. csvTable.Rows.Select(row => map(new CsvRowReader(csvTable, row, Errors)))];
        }
    }
}
