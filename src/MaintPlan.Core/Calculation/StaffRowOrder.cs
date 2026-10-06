using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>人員区分の行を並べる順。種別の順とし、種別の中では種別ごとの合計の行を先に、区分ごとの行を表示順・ID の順に置く。</summary>
public static class StaffRowOrder
{
    /// <summary>種別ごとの合計の行の順。</summary>
    public static (StaffKind Kind, bool IsCategory, int DisplayOrder, int Id) Of(StaffKind kind) => (kind, false, 0, 0);

    /// <summary>区分ごとの行の順。</summary>
    public static (StaffKind Kind, bool IsCategory, int DisplayOrder, int Id) Of(StaffCategory category) =>
        (category.Kind, true, category.DisplayOrder, category.Id);
}
