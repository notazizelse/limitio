using System.Collections.Concurrent;
using LimitIO.Core.Models;
using LimitIO.Core.Storage;
using LimitIO.Core.Time;
using LimitIO.Service.Configuration;
using Microsoft.Extensions.Logging;

namespace LimitIO.Service.Accounting;

/// <summary>
/// Turns a stream of <see cref="ResolvedFlow"/> observations into accrued time-per-rule. Accrual is
/// interval-based (credit a fixed tick's worth of time to a rule once per tick if any flow matching it
/// was seen recently) rather than per-packet/per-byte, because what matters for a Screen-Time-style limit
/// is "was this thing in active use," not bandwidth consumed - and interval accrual naturally avoids
/// double-counting when several connections to the same rule are open at once (e.g. multiple browser tabs
/// on the same limited site only advance that rule's clock once per tick, not once per tab).
/// </summary>
public sealed class UsageAccountant
{
    private static readonly TimeSpan FlowIdleTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    private sealed record FlowState(string? ProcessName, string? Domain, DateTimeOffset LastSeen);

    private readonly ConfigCache _configCache;
    private readonly UsageStore _usageStore;
    private readonly IClock _clock;
    private readonly ILogger<UsageAccountant> _logger;
    private readonly ConcurrentDictionary<FlowKey, FlowState> _activeFlows = new();

    public UsageAccountant(ConfigCache configCache, UsageStore usageStore, IClock clock, ILogger<UsageAccountant> logger)
    {
        _configCache = configCache;
        _usageStore = usageStore;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Called from the capture engine's <c>FlowObserved</c> event for every attributed packet.</summary>
    public void OnFlowObserved(ResolvedFlow flow)
    {
        _activeFlows[flow.Key] = new FlowState(flow.ProcessName, flow.Domain, flow.ObservedAt);
    }

    /// <summary>Called once per <see cref="TickInterval"/> to credit elapsed time to whichever rules currently have active matching traffic.</summary>
    public void Tick()
    {
        var now = _clock.UtcNow;
        PruneStaleFlows(now);

        var config = _configCache.Current;
        if (config.Rules.Count == 0 || _activeFlows.IsEmpty)
        {
            return;
        }

        var matchedRuleIds = new HashSet<Guid>();
        foreach (var flow in _activeFlows.Values)
        {
            foreach (var rule in config.Rules)
            {
                if (rule.Matches(flow.Domain, flow.ProcessName))
                {
                    matchedRuleIds.Add(rule.Id);
                }
            }
        }

        var localNow = _clock.LocalNow;
        foreach (var ruleId in matchedRuleIds)
        {
            var rule = config.Rules.Find(r => r.Id == ruleId);
            if (rule is null)
            {
                continue;
            }

            var periodDate = ResetScheduling.CurrentPeriodDate(localNow, rule.ResetTime);
            var record = _usageStore.GetOrCreate(ruleId, periodDate);
            record.Consumed += TickInterval;
            _usageStore.Save(record);
        }
    }

    private void PruneStaleFlows(DateTimeOffset now)
    {
        foreach (var (key, state) in _activeFlows)
        {
            if (now - state.LastSeen > FlowIdleTimeout)
            {
                _activeFlows.TryRemove(key, out _);
            }
        }
    }
}
