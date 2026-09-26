using LimitIO.Core.Storage;
using LimitIO.Service.Enforcement;
using LimitIO.Service.Tests.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Enforcement;

[TestClass]
public class GraceManagerTests
{
    private string _tempDir = null!;
    private UsageStore _usageStore = null!;
    private FakeClock _clock = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LimitIOTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _usageStore = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        _clock = new FakeClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _usageStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void TryStartGrace_FirstTimeToday_Succeeds()
    {
        var manager = new GraceManager(_usageStore, _clock);
        var ruleId = Guid.NewGuid();

        var result = manager.TryStartGrace(ruleId, TimeSpan.Zero);

        Assert.AreEqual(GraceResult.Ok, result);
        Assert.IsTrue(manager.IsGraceActive(ruleId));
    }

    [TestMethod]
    public void TryStartGrace_SecondTimeSameDay_Fails()
    {
        var manager = new GraceManager(_usageStore, _clock);
        var ruleId = Guid.NewGuid();

        manager.TryStartGrace(ruleId, TimeSpan.Zero);
        _clock.Advance(TimeSpan.FromMinutes(5)); // grace expired, but still same day
        var second = manager.TryStartGrace(ruleId, TimeSpan.Zero);

        Assert.AreEqual(GraceResult.AlreadyUsedToday, second);
    }

    [TestMethod]
    public void IsGraceActive_AfterOneMinute_ExpiresAutomatically()
    {
        var manager = new GraceManager(_usageStore, _clock);
        var ruleId = Guid.NewGuid();

        manager.TryStartGrace(ruleId, TimeSpan.Zero);
        Assert.IsTrue(manager.IsGraceActive(ruleId));

        _clock.Advance(TimeSpan.FromSeconds(61));

        Assert.IsFalse(manager.IsGraceActive(ruleId));
    }

    [TestMethod]
    public void TryStartGrace_SurvivesRestart_StillOncePerDay()
    {
        // Simulates a service restart: a fresh GraceManager reading the same persisted UsageStore
        // must not grant a second grace for a rule that already used its grace today.
        var ruleId = Guid.NewGuid();
        var firstManager = new GraceManager(_usageStore, _clock);
        firstManager.TryStartGrace(ruleId, TimeSpan.Zero);

        var secondManager = new GraceManager(_usageStore, _clock);
        var result = secondManager.TryStartGrace(ruleId, TimeSpan.Zero);

        Assert.AreEqual(GraceResult.AlreadyUsedToday, result);
    }

    [TestMethod]
    public void TryStartGrace_NextDay_SucceedsAgain()
    {
        var manager = new GraceManager(_usageStore, _clock);
        var ruleId = Guid.NewGuid();

        manager.TryStartGrace(ruleId, TimeSpan.Zero);
        _clock.Advance(TimeSpan.FromDays(1));

        var result = manager.TryStartGrace(ruleId, TimeSpan.Zero);

        Assert.AreEqual(GraceResult.Ok, result);
    }

    [TestMethod]
    public void TryStartGrace_CalledAgainWhileActive_IsIdempotent()
    {
        var manager = new GraceManager(_usageStore, _clock);
        var ruleId = Guid.NewGuid();

        var first = manager.TryStartGrace(ruleId, TimeSpan.Zero);
        var second = manager.TryStartGrace(ruleId, TimeSpan.Zero);

        Assert.AreEqual(GraceResult.Ok, first);
        Assert.AreEqual(GraceResult.Ok, second);
    }
}
