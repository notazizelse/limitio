using System.Net;
using LimitIO.Core.Models;
using LimitIO.Core.Storage;
using LimitIO.Service.Enforcement;
using LimitIO.Service.Configuration;
using LimitIO.Service.Tests.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Enforcement;

[TestClass]
public class LimitEnforcerTests
{
    private string _tempDir = null!;
    private ConfigStore _configStore = null!;
    private ConfigCache _configCache = null!;
    private UsageStore _usageStore = null!;
    private FakeClock _clock = null!;
    private GraceManager _graceManager = null!;
    private LimitEnforcer _enforcer = null!;

    private static readonly FlowKey SampleFlow = new(
        IsTcp: true,
        LocalAddress: IPAddress.Parse("192.168.1.5"),
        LocalPort: 51000,
        RemoteAddress: IPAddress.Parse("93.184.216.34"),
        RemotePort: 443);

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LimitIOTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _configStore = new ConfigStore(Path.Combine(_tempDir, "config.json"));
        _configCache = new ConfigCache(_configStore);
        _usageStore = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        _clock = new FakeClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        _graceManager = new GraceManager(_usageStore, _clock);
        _enforcer = new LimitEnforcer(_configCache, _usageStore, _graceManager, _clock);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _usageStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private LimitRule AddRule(LimitTargetType type, string target, TimeSpan budget)
    {
        var rule = LimitRule.Create(type, target, budget);
        _configCache.Update(c => c.Rules.Add(rule));
        return rule;
    }

    [TestMethod]
    public void ShouldBlock_NoMatchingRule_ReturnsFalse()
    {
        AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "example.com");

        Assert.IsFalse(block);
    }

    [TestMethod]
    public void ShouldBlock_MatchingRuleUnderBudget_ReturnsFalse()
    {
        var rule = AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(10);
        _usageStore.Save(record);

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "instagram.com");

        Assert.IsFalse(block);
    }

    [TestMethod]
    public void ShouldBlock_MatchingRuleOverBudget_ReturnsTrue()
    {
        var rule = AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(31);
        _usageStore.Save(record);

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "instagram.com");

        Assert.IsTrue(block);
    }

    [TestMethod]
    public void ShouldBlock_OverBudgetButGraceActive_ReturnsFalse()
    {
        var rule = AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(31);
        _usageStore.Save(record);
        _graceManager.TryStartGrace(rule.Id, rule.ResetTime);

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "instagram.com");

        Assert.IsFalse(block);
    }

    [TestMethod]
    public void ShouldBlock_DisabledRule_NeverBlocks()
    {
        var rule = AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Find(r => r.Id == rule.Id)!.Enabled = false);
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(100);
        _usageStore.Save(record);

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "instagram.com");

        Assert.IsFalse(block);
    }

    [TestMethod]
    public void ShouldBlock_ProcessRuleOverBudget_BlocksRegardlessOfDomain()
    {
        var rule = AddRule(LimitTargetType.ProcessName, "telegram.exe", TimeSpan.FromMinutes(45));
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(46);
        _usageStore.Save(record);

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "telegram", domain: null);

        Assert.IsTrue(block);
    }

    [TestMethod]
    public void ShouldBlock_MonitoringPaused_NeverBlocksEvenOverBudget()
    {
        var rule = AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(60);
        _usageStore.Save(record);
        _configCache.Update(c => c.MonitoringPausedUntil = _clock.UtcNow.AddMinutes(10));

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "instagram.com");

        Assert.IsFalse(block);
    }

    [TestMethod]
    public void ShouldBlock_PauseExpired_ResumesBlocking()
    {
        var rule = AddRule(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        record.Consumed = TimeSpan.FromMinutes(60);
        _usageStore.Save(record);
        _configCache.Update(c => c.MonitoringPausedUntil = _clock.UtcNow.AddMinutes(-1));

        var block = _enforcer.ShouldBlock(SampleFlow, 1234, "chrome", "instagram.com");

        Assert.IsTrue(block);
    }
}
