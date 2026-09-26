using LimitIO.Core.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Core.Tests;

[TestClass]
public class ResetSchedulingTests
{
    [TestMethod]
    public void NextOccurrence_BeforeResetTimeToday_ReturnsTodayAtResetTime()
    {
        var now = new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero);

        var next = ResetScheduling.NextOccurrence(now, TimeSpan.Zero);

        Assert.AreEqual(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero), next);
    }

    [TestMethod]
    public void NextOccurrence_AfterResetTimeToday_ReturnsTomorrow()
    {
        var now = new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero);

        var next = ResetScheduling.NextOccurrence(now, TimeSpan.FromHours(6));

        Assert.AreEqual(new DateTimeOffset(2026, 3, 16, 6, 0, 0, TimeSpan.Zero), next);
    }

    [TestMethod]
    public void NextOccurrence_ExactlyAtResetTime_RollsToNextDay()
    {
        var now = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

        var next = ResetScheduling.NextOccurrence(now, TimeSpan.Zero);

        Assert.AreEqual(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero), next);
    }

    [TestMethod]
    public void CurrentPeriodDate_BeforeMidnightReset_IsToday()
    {
        var now = new DateTimeOffset(2026, 3, 15, 23, 0, 0, TimeSpan.Zero);

        var period = ResetScheduling.CurrentPeriodDate(now, TimeSpan.Zero);

        Assert.AreEqual(new DateOnly(2026, 3, 15), period);
    }

    [TestMethod]
    public void CurrentPeriodDate_BeforeConfiguredResetTime_IsPreviousDay()
    {
        // Reset time of 4am: at 2am, we're still in "yesterday's" accounting period.
        var now = new DateTimeOffset(2026, 3, 15, 2, 0, 0, TimeSpan.Zero);

        var period = ResetScheduling.CurrentPeriodDate(now, TimeSpan.FromHours(4));

        Assert.AreEqual(new DateOnly(2026, 3, 14), period);
    }

    [TestMethod]
    public void CurrentPeriodDate_AfterConfiguredResetTime_IsToday()
    {
        var now = new DateTimeOffset(2026, 3, 15, 5, 0, 0, TimeSpan.Zero);

        var period = ResetScheduling.CurrentPeriodDate(now, TimeSpan.FromHours(4));

        Assert.AreEqual(new DateOnly(2026, 3, 15), period);
    }
}
