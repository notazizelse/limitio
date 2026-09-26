using System.Collections.Concurrent;
using LimitIO.Core.Storage;
using LimitIO.Core.Time;

namespace LimitIO.Service.Enforcement;

/// <summary>
/// The once-per-day, 60-second "ignore this limit" valve - deliberately usable with no password, mirroring
/// how iOS lets you tap "One More Minute" without the Screen Time passcode. The once-per-day limit is
/// enforced here, server-side, keyed off <see cref="Core.Models.UsageRecord.GraceUsedToday"/> which is
/// persisted the instant a grace starts (not when it expires) - so a service restart mid-grace can never
/// be used to claim a second free grace for the same rule on the same day.
/// </summary>
public sealed class GraceManager
{
    public static readonly TimeSpan GraceDuration = TimeSpan.FromMinutes(1);

    private readonly UsageStore _usageStore;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _activeGraceExpiry = new();

    public GraceManager(UsageStore usageStore, IClock clock)
    {
        _usageStore = usageStore;
        _clock = clock;
    }

    public bool IsGraceActive(Guid ruleId)
    {
        if (_activeGraceExpiry.TryGetValue(ruleId, out var expiry))
        {
            if (_clock.UtcNow < expiry)
            {
                return true;
            }
            _activeGraceExpiry.TryRemove(ruleId, out _);
        }
        return false;
    }

    public GraceResult TryStartGrace(Guid ruleId, TimeSpan ruleResetTimeOfDay)
    {
        if (IsGraceActive(ruleId))
        {
            return GraceResult.Ok;
        }

        var periodDate = ResetScheduling.CurrentPeriodDate(_clock.LocalNow, ruleResetTimeOfDay);
        var record = _usageStore.GetOrCreate(ruleId, periodDate);
        if (record.GraceUsedToday)
        {
            return GraceResult.AlreadyUsedToday;
        }

        record.GraceUsedToday = true;
        _usageStore.Save(record);
        _activeGraceExpiry[ruleId] = _clock.UtcNow + GraceDuration;
        return GraceResult.Ok;
    }

    public bool WasGraceUsedToday(Guid ruleId, TimeSpan ruleResetTimeOfDay)
    {
        var periodDate = ResetScheduling.CurrentPeriodDate(_clock.LocalNow, ruleResetTimeOfDay);
        return _usageStore.GetOrCreate(ruleId, periodDate).GraceUsedToday;
    }
}

public enum GraceResult
{
    Ok,
    AlreadyUsedToday,
}
