using System.Globalization;
using MaintPlan.Core.Model;

namespace MaintPlan.Core.Calculation;

/// <summary>期間の数え方。日数は暦日で数え、開始日と終了日を含める。</summary>
public static class Period
{
    /// <summary>開始日から終了日までの期間(両端を含む)が、各月に何日入るか。月の早い順に返す。</summary>
    public static IReadOnlyList<MonthDays> DaysByMonth(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException(
                $"終了日 {end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} が開始日 {start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} より前です。",
                nameof(end));
        }

        var first = YearMonth.Of(start);
        var last = YearMonth.Of(end);
        var days = new List<MonthDays>();
        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            var from = month == first ? start : month.FirstDay;
            var to = month == last ? end : month.LastDay;
            days.Add(new MonthDays(month, to.DayNumber - from.DayNumber + 1));
        }

        return days;
    }
}
