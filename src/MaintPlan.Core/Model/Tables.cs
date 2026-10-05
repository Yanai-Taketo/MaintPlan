namespace MaintPlan.Core.Model;

// 設計書3章のテーブルのうち、計算に使う9つ。
// 金額は円の整数、作業日数は10倍した整数で持つ。
// 共通の列のうち作成日時・作成者・更新日時・更新者は工程2以降で扱う。

/// <summary>工事。</summary>
public sealed record ConstructionWork
{
    public required int Id { get; init; }

    /// <summary>管理番号。</summary>
    public required string ManagementNumber { get; init; }

    /// <summary>工事名。</summary>
    public required string Name { get; init; }

    public required WorkStatus Status { get; init; }

    /// <summary>実施予定の開始日。未定なら null。</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>実施予定の終了日。未定なら null。</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>実施年度。開始日と終了日が空欄で、年度だけ決まっているときに入る。</summary>
    public int? PlannedFiscalYear { get; init; }

    public string? EquipmentName { get; init; }

    /// <summary>設置場所。</summary>
    public string? Location { get; init; }

    /// <summary>担当者。</summary>
    public string? PersonInCharge { get; init; }

    /// <summary>担当部署。</summary>
    public string? Department { get; init; }

    /// <summary>協力会社名。</summary>
    public string? ContractorName { get; init; }

    public Priority? Priority { get; init; }

    /// <summary>備考。</summary>
    public string? Remarks { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>費用内訳。工事1件に費用区分ごとに1件。</summary>
public sealed record CostItem
{
    public required int Id { get; init; }

    public required int ConstructionWorkId { get; init; }

    public required CostCategory Category { get; init; }

    /// <summary>計上の考え方。</summary>
    public required RecognitionMethod Recognition { get; init; }

    /// <summary>工事番号。</summary>
    public string? WorkNumber { get; init; }

    /// <summary>見積額。未入力なら null。</summary>
    public long? EstimateAmount { get; init; }

    /// <summary>その他の費用(予算額の計算に使う材料費などの合計)。</summary>
    public long? OtherCost { get; init; }

    /// <summary>その他の費用の内容。</summary>
    public string? OtherCostDescription { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>予算年割。費用内訳1件に年度ごとに1件。</summary>
public sealed record AnnualBudget
{
    public required int Id { get; init; }

    public required int CostItemId { get; init; }

    public required int FiscalYear { get; init; }

    /// <summary>予算額(年割額)。</summary>
    public required long Amount { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>実績。</summary>
public sealed record ActualCost
{
    public required int Id { get; init; }

    public required int CostItemId { get; init; }

    /// <summary>計上する月。</summary>
    public required YearMonth Month { get; init; }

    /// <summary>実績額。</summary>
    public required long Amount { get; init; }

    /// <summary>摘要。</summary>
    public string? Note { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>月別修正。出来高の費用内訳の予算額と見積額に使う。</summary>
public sealed record MonthlyOverride
{
    public required int Id { get; init; }

    public required int CostItemId { get; init; }

    /// <summary>金額の種類。予算額か見積額。</summary>
    public required AmountKind Kind { get; init; }

    public required YearMonth Month { get; init; }

    /// <summary>その月に計上する金額。</summary>
    public required long Amount { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>作業明細。人工は保存せず、人数×作業日数で計算する。</summary>
public sealed record LaborLine
{
    public required int Id { get; init; }

    public required int CostItemId { get; init; }

    public required int StaffCategoryId { get; init; }

    /// <summary>内容(準備、本作業など)。</summary>
    public string? Description { get; init; }

    /// <summary>開始日。未定なら null。</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>終了日。未定なら null。</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>人数。</summary>
    public required int Headcount { get; init; }

    /// <summary>作業日数を10倍した整数。10.5日なら105。</summary>
    public required long WorkDaysTenths { get; init; }

    /// <summary>人工(人数×作業日数)を10倍した整数。</summary>
    public long ManDaysTenths => Headcount * WorkDaysTenths;

    public bool IsDeleted { get; init; }
}

/// <summary>人員区分。</summary>
public sealed record StaffCategory
{
    public required int Id { get; init; }

    /// <summary>種別(直営・協力会社)。</summary>
    public required StaffKind Kind { get; init; }

    /// <summary>区分名。</summary>
    public required string Name { get; init; }

    /// <summary>予算額の計算に含めるか。</summary>
    public required bool IncludeInBudget { get; init; }

    /// <summary>表示順。</summary>
    public required int DisplayOrder { get; init; }

    /// <summary>使用中か。計算には影響しない。</summary>
    public required bool InUse { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>単価。人員区分1件に年度ごとに1件。</summary>
public sealed record UnitRate
{
    public required int Id { get; init; }

    public required int StaffCategoryId { get; init; }

    public required int FiscalYear { get; init; }

    /// <summary>単価(円/人日)。</summary>
    public required long Rate { get; init; }

    public bool IsDeleted { get; init; }
}

/// <summary>予算枠。年度と費用区分の組み合わせで1件。</summary>
public sealed record BudgetFrame
{
    public required int Id { get; init; }

    public required int FiscalYear { get; init; }

    public required CostCategory Category { get; init; }

    /// <summary>予算枠額(令達額)。</summary>
    public required long Amount { get; init; }

    public bool IsDeleted { get; init; }
}
