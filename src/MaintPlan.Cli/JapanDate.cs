namespace MaintPlan.Cli;

/// <summary>日本時間(UTC+9)の日付。</summary>
public static class JapanDate
{
    /// <summary>日本には夏時間がないので、時差はいつも9時間。</summary>
    private static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    /// <summary>time の瞬間の、日本時間の日付。動かしたコンピューターのタイムゾーンにはよらない。</summary>
    public static DateOnly Of(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(Offset).DateTime);
}
