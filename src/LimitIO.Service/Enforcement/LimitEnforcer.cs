using LimitIO.Core.Models;
using LimitIO.Core.Storage;
using LimitIO.Core.Time;
using LimitIO.Service.Capture;
using LimitIO.Service.Configuration;

namespace LimitIO.Service.Enforcement;

/// <summary>
/// The live block-decision gate consulted by <see cref="PacketCaptureEngine"/> for every observed packet.
/// A flow is blocked when it matches an enabled rule whose consumed time for the current period already
/// meets or exceeds its daily budget, and that rule isn't currently under an active one-minute grace.
/// Not reinjecting the packet *is* the enforcement action - see docs/ARCHITECTURE.md for why this looks
/// like a stall to the blocked app rather than a clean error.
/// </summary>
public sealed class LimitEnforcer : IFlowBlockDecider
{
    private readonly ConfigCache _configCache;
    private readonly UsageStore _usageStore;
    private readonly GraceManager _graceManager;
    private readonly IClock _clock;

    public LimitEnforcer(ConfigCache configCache, UsageStore usageStore, GraceManager graceManager, IClock clock)
    {
        _configCache = configCache;
        _usageStore = usageStore;
        _graceManager = graceManager;
        _clock = clock;
    }

    public bool ShouldBlock(FlowKey key, int processId, string? processName, string? domain)
    {
        var config = _configCache.Current;

        if (config.MonitoringPausedUntil is { } pausedUntil && pausedUntil > _clock.UtcNow)
        {
            return false;
        }

        foreach (var rule in config.Rules)
        {
            if (!rule.Matches(domain, processName))
            {
                continue;
            }

            if (_graceManager.IsGraceActive(rule.Id))
            {
                continue;
            }

            var periodDate = ResetScheduling.CurrentPeriodDate(_clock.LocalNow, rule.ResetTime);
            var record = _usageStore.GetOrCreate(rule.Id, periodDate);
            if (record.Consumed >= rule.DailyBudget)
            {
                return true;
            }
        }

        return false;
    }
}
