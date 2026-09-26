using System.Net;
using LimitIO.Core.Models;
using LimitIO.Core.Storage;
using LimitIO.Service.Accounting;
using LimitIO.Service.Configuration;
using LimitIO.Service.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LimitIO.Service.Tests.Accounting;

[TestClass]
public class UsageAccountantTests
{
    private string _tempDir = null!;
    private ConfigStore _configStore = null!;
    private ConfigCache _configCache = null!;
    private UsageStore _usageStore = null!;
    private FakeClock _clock = null!;
    private UsageAccountant _accountant = null!;

    private static ResolvedFlow MakeFlow(string? domain, string? processName, DateTimeOffset now) => new(
        Key: new FlowKey(true, IPAddress.Parse("10.0.0.5"), 55000, IPAddress.Parse("1.2.3.4"), 443),
        ProcessId: 100,
        ProcessName: processName,
        Domain: domain,
        ObservedAt: now);

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LimitIOTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _configStore = new ConfigStore(Path.Combine(_tempDir, "config.json"));
        _configCache = new ConfigCache(_configStore);
        _usageStore = new UsageStore(Path.Combine(_tempDir, "usage.db"));
        _clock = new FakeClock(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        _accountant = new UsageAccountant(_configCache, _usageStore, _clock, NullLogger<UsageAccountant>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _usageStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void Tick_ActiveMatchingFlow_AccruesOneTickOfConsumedTime()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Add(rule));

        _accountant.OnFlowObserved(MakeFlow("instagram.com", "chrome", _clock.UtcNow));
        _accountant.Tick();

        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        Assert.AreEqual(UsageAccountant.TickInterval, record.Consumed);
    }

    [TestMethod]
    public void Tick_MultipleTicks_AccruesCumulatively()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Add(rule));

        _accountant.OnFlowObserved(MakeFlow("instagram.com", "chrome", _clock.UtcNow));
        _accountant.Tick();
        _accountant.OnFlowObserved(MakeFlow("instagram.com", "chrome", _clock.UtcNow));
        _accountant.Tick();

        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        Assert.AreEqual(UsageAccountant.TickInterval * 2, record.Consumed);
    }

    [TestMethod]
    public void Tick_MultipleFlowsSameRule_DoesNotDoubleCount()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Add(rule));

        // Two different "tabs" (different local ports) both hitting instagram.com in the same tick.
        var flowA = MakeFlow("instagram.com", "chrome", _clock.UtcNow) with
        {
            Key = new FlowKey(true, IPAddress.Parse("10.0.0.5"), 55000, IPAddress.Parse("1.2.3.4"), 443),
        };
        var flowB = MakeFlow("instagram.com", "chrome", _clock.UtcNow) with
        {
            Key = new FlowKey(true, IPAddress.Parse("10.0.0.5"), 55001, IPAddress.Parse("1.2.3.4"), 443),
        };

        _accountant.OnFlowObserved(flowA);
        _accountant.OnFlowObserved(flowB);
        _accountant.Tick();

        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        Assert.AreEqual(UsageAccountant.TickInterval, record.Consumed);
    }

    [TestMethod]
    public void Tick_NoActiveFlows_AccruesNothing()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Add(rule));

        _accountant.Tick();

        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        Assert.AreEqual(TimeSpan.Zero, record.Consumed);
    }

    [TestMethod]
    public void Tick_StaleFlow_StopsAccruingAfterIdleTimeout()
    {
        var rule = LimitRule.Create(LimitTargetType.Domain, "instagram.com", TimeSpan.FromMinutes(30));
        _configCache.Update(c => c.Rules.Add(rule));

        _accountant.OnFlowObserved(MakeFlow("instagram.com", "chrome", _clock.UtcNow));
        _accountant.Tick();

        _clock.Advance(TimeSpan.FromSeconds(25)); // beyond the 20s idle timeout, no new observation
        _accountant.Tick();

        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        Assert.AreEqual(UsageAccountant.TickInterval, record.Consumed); // only the first tick counted
    }

    [TestMethod]
    public void Tick_ProcessRuleMatch_AccruesEvenWithNullDomain()
    {
        var rule = LimitRule.Create(LimitTargetType.ProcessName, "telegram.exe", TimeSpan.FromMinutes(45));
        _configCache.Update(c => c.Rules.Add(rule));

        _accountant.OnFlowObserved(MakeFlow(domain: null, processName: "telegram", _clock.UtcNow));
        _accountant.Tick();

        var record = _usageStore.GetOrCreate(rule.Id, DateOnly.FromDateTime(_clock.LocalNow.Date));
        Assert.AreEqual(UsageAccountant.TickInterval, record.Consumed);
    }
}
