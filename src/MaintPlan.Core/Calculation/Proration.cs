namespace MaintPlan.Core.Calculation;

/// <summary>
/// 暦日の比での割り振り。
/// 最終の先以外は「元の値 × その先の日数」を全体の日数で割った商(切り捨て)とし、
/// 最終の先には「元の値 − ほかの先の合計」を入れる。割り振った値の合計は、元の値と必ず一致する。
/// </summary>
internal static class Proration
{
    /// <summary>value を、先ごとの日数の比で割り振る。最終の先は、キーが最も遅い先とする。結果は shares と同じ順に返す。</summary>
    public static IReadOnlyList<(TKey Key, long Value)> ByDays<TKey>(long value, IReadOnlyList<(TKey Key, long Days)> shares)
        where TKey : IComparable<TKey>
    {
        if (shares.Count == 0)
        {
            throw new ArgumentException("割り振り先がありません。", nameof(shares));
        }

        if (shares.Any(share => share.Days <= 0))
        {
            throw new ArgumentException("割り振り先の日数は1以上です。", nameof(shares));
        }

        var totalDays = shares.Sum(share => share.Days);
        var finalIndex = 0;
        for (var index = 1; index < shares.Count; index++)
        {
            if (shares[index].Key.CompareTo(shares[finalIndex].Key) > 0)
            {
                finalIndex = index;
            }
        }

        var values = new (TKey Key, long Value)[shares.Count];
        var allocated = 0L;
        for (var index = 0; index < shares.Count; index++)
        {
            if (index == finalIndex)
            {
                continue;
            }

            var share = checked(value * shares[index].Days) / totalDays;
            values[index] = (shares[index].Key, share);
            allocated += share;
        }

        values[finalIndex] = (shares[finalIndex].Key, value - allocated);
        return values;
    }
}
