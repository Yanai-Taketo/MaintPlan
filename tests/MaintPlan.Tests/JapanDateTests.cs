using System.Globalization;
using MaintPlan.Cli;

namespace MaintPlan.Tests;

/// <summary>
/// 集計基準日を省いたときの当日に使う、日本時間(UTC+9)の日付を確かめる。
/// 日本には夏時間がないので、期待する日付は、UTC の時刻に9時間を足した日付になる。
/// </summary>
public sealed class JapanDateTests
{
    [Theory]
    // 日本時間の2027-03-31 23:59:59。年度の最後
    [InlineData("2027-03-31T14:59:59Z", "2027-03-31")]
    // 日本時間の2027-04-01 00:00:00。年度が変わる
    [InlineData("2027-03-31T15:00:00Z", "2027-04-01")]
    // 日本時間の2026-12-31 23:59:59と2027-01-01 00:00:00。年が変わる
    [InlineData("2026-12-31T14:59:59Z", "2026-12-31")]
    [InlineData("2026-12-31T15:00:00Z", "2027-01-01")]
    public void Date_is_the_date_in_japan_time(string time, string date)
    {
        Assert.Equal(DateOf(date), JapanDate.Of(TimeOf(time)));
    }

    /// <summary>同じ瞬間なら、どの時差で表しても、日本時間の日付になる。</summary>
    [Theory]
    // 2027-03-31T14:59:59Z を、日本時間・-05:00(その時差では3月31日)・+14:00(その時差では4月1日)で表す
    [InlineData("2027-03-31T23:59:59+09:00", "2027-03-31")]
    [InlineData("2027-03-31T09:59:59-05:00", "2027-03-31")]
    [InlineData("2027-04-01T04:59:59+14:00", "2027-03-31")]
    // 2027-03-31T15:00:00Z を、日本時間・-05:00(その時差では3月31日)・+14:00(その時差では4月1日)で表す
    [InlineData("2027-04-01T00:00:00+09:00", "2027-04-01")]
    [InlineData("2027-03-31T10:00:00-05:00", "2027-04-01")]
    [InlineData("2027-04-01T05:00:00+14:00", "2027-04-01")]
    public void Date_does_not_depend_on_the_offset(string time, string date)
    {
        Assert.Equal(DateOf(date), JapanDate.Of(TimeOf(time)));
    }

    /// <summary>時差を付けて書いた時刻。時差は書いたとおりに持つ。</summary>
    private static DateTimeOffset TimeOf(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static DateOnly DateOf(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
