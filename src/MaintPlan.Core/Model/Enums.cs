namespace MaintPlan.Core.Model;

/// <summary>工事の状態。</summary>
public enum WorkStatus
{
    /// <summary>計画中</summary>
    Planning,
    /// <summary>承認済み</summary>
    Approved,
    /// <summary>発注済み</summary>
    Ordered,
    /// <summary>施工中</summary>
    InProgress,
    /// <summary>完了</summary>
    Completed,
    /// <summary>中止</summary>
    Cancelled,
}

/// <summary>費用区分。</summary>
public enum CostCategory
{
    /// <summary>修繕費</summary>
    Repair,
    /// <summary>設備投資</summary>
    CapitalInvestment,
}

/// <summary>計上の考え方。</summary>
public enum RecognitionMethod
{
    /// <summary>出来高</summary>
    Progress,
    /// <summary>完成工事高</summary>
    Completion,
}

/// <summary>金額の種類。</summary>
public enum AmountKind
{
    /// <summary>予算額</summary>
    Budget,
    /// <summary>見積額</summary>
    Estimate,
    /// <summary>実績額</summary>
    Actual,
}

/// <summary>人員区分の種別。</summary>
public enum StaffKind
{
    /// <summary>直営</summary>
    Direct,
    /// <summary>協力会社</summary>
    Contractor,
}

/// <summary>工事の優先度。</summary>
public enum Priority
{
    /// <summary>高</summary>
    High,
    /// <summary>中</summary>
    Medium,
    /// <summary>低</summary>
    Low,
}

/// <summary>計算に使う9つのテーブル。</summary>
public enum PlanTable
{
    /// <summary>工事</summary>
    ConstructionWork,
    /// <summary>費用内訳</summary>
    CostItem,
    /// <summary>予算年割</summary>
    AnnualBudget,
    /// <summary>実績</summary>
    ActualCost,
    /// <summary>月別修正</summary>
    MonthlyOverride,
    /// <summary>作業明細</summary>
    LaborLine,
    /// <summary>人員区分</summary>
    StaffCategory,
    /// <summary>単価</summary>
    UnitRate,
    /// <summary>予算枠</summary>
    BudgetFrame,
}
