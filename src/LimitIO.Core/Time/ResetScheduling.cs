namespace LimitIO.Core.Time;

/// <summary>
/// Computes "next occurrence of local time-of-day X" so daily resets can be scheduled with a single
/// timer firing at an absolute instant, rather than a naive 24-hour repeating timer that would drift
/// across a DST transition.
/// </summary>
public static class ResetScheduling
{
    public static DateTimeOffset NextOccurrence(DateTimeOffset localNow, TimeSpan timeOfDay)
    {
        var candidate = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset) + timeOfDay;
        if (candidate <= localNow)
        {
            candidate = candidate.AddDays(1);
        }
        return candidate;
    }

    public static DateOnly CurrentPeriodDate(DateTimeOffset localNow, TimeSpan resetTimeOfDay)
    {
        var todaysReset = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset) + resetTimeOfDay;
        var date = DateOnly.FromDateTime(localNow.Date);
        return localNow < todaysReset ? date.AddDays(-1) : date;
    }
}
