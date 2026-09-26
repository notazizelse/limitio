using LimitIO.Core.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Core.Tests;

[TestClass]
public class UsageStoreTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LimitIOTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void GetOrCreate_NoExistingRow_ReturnsZeroedRecord()
    {
        using var store = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        var ruleId = Guid.NewGuid();

        var record = store.GetOrCreate(ruleId, DateOnly.FromDateTime(DateTime.Today));

        Assert.AreEqual(TimeSpan.Zero, record.Consumed);
        Assert.IsFalse(record.GraceUsedToday);
    }

    [TestMethod]
    public void SaveThenGetOrCreate_RoundTripsConsumedAndGrace()
    {
        using var store = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        var ruleId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.Today);

        var record = store.GetOrCreate(ruleId, date);
        record.Consumed = TimeSpan.FromMinutes(17);
        record.GraceUsedToday = true;
        store.Save(record);

        var reloaded = store.GetOrCreate(ruleId, date);

        Assert.AreEqual(TimeSpan.FromMinutes(17), reloaded.Consumed);
        Assert.IsTrue(reloaded.GraceUsedToday);
    }

    [TestMethod]
    public void Save_DifferentDatesForSameRule_DoNotOverwriteEachOther()
    {
        using var store = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        var ruleId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);

        var todayRecord = store.GetOrCreate(ruleId, today);
        todayRecord.Consumed = TimeSpan.FromMinutes(10);
        store.Save(todayRecord);

        var yesterdayRecord = store.GetOrCreate(ruleId, yesterday);
        yesterdayRecord.Consumed = TimeSpan.FromMinutes(55);
        store.Save(yesterdayRecord);

        Assert.AreEqual(TimeSpan.FromMinutes(10), store.GetOrCreate(ruleId, today).Consumed);
        Assert.AreEqual(TimeSpan.FromMinutes(55), store.GetOrCreate(ruleId, yesterday).Consumed);
    }

    [TestMethod]
    public void GetAllForDate_ReturnsOnlyThatDatesRecords()
    {
        using var store = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        var ruleA = Guid.NewGuid();
        var ruleB = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);

        var a = store.GetOrCreate(ruleA, today);
        a.Consumed = TimeSpan.FromMinutes(5);
        store.Save(a);

        var b = store.GetOrCreate(ruleB, yesterday);
        b.Consumed = TimeSpan.FromMinutes(9);
        store.Save(b);

        var todaysRecords = store.GetAllForDate(today);

        Assert.AreEqual(1, todaysRecords.Count);
        Assert.IsTrue(todaysRecords.ContainsKey(ruleA));
    }

    [TestMethod]
    public void PruneOlderThan_RemovesOnlyOldRows()
    {
        using var store = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        var ruleId = Guid.NewGuid();
        var old = new DateOnly(2020, 1, 1);
        var recent = DateOnly.FromDateTime(DateTime.Today);

        store.Save(store.GetOrCreate(ruleId, old));
        store.Save(store.GetOrCreate(ruleId, recent));

        store.PruneOlderThan(recent);

        Assert.AreEqual(0, store.GetAllForDate(old).Count);
        Assert.AreEqual(1, store.GetAllForDate(recent).Count);
    }
}
