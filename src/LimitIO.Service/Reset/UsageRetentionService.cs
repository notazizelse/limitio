using LimitIO.Core.Storage;
using LimitIO.Core.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LimitIO.Service.Reset;

/// <summary>
/// Note on daily "resets": there is deliberately no timer-based reset-at-midnight logic anywhere in this
/// service. <see cref="UsageStore"/> keys every record by (RuleId, Date), so a new calendar day (per each
/// rule's own <c>ResetTime</c>, via <see cref="ResetScheduling.CurrentPeriodDate"/>) automatically starts
/// from a zeroed record - "reset" falls out of the storage model for free, with no DST-drift-prone timer
/// to get wrong. What *does* need periodic maintenance is trimming old rows so the usage database doesn't
/// grow forever; that's all this background service does.
/// </summary>
public sealed class UsageRetentionService : BackgroundService
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UsageStore _usageStore;
    private readonly IClock _clock;
    private readonly ILogger<UsageRetentionService> _logger;

    public UsageRetentionService(UsageStore usageStore, IClock clock, ILogger<UsageRetentionService> logger)
    {
        _usageStore = usageStore;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateOnly.FromDateTime(_clock.LocalNow.Date).AddDays(-RetentionPeriod.Days);
                _usageStore.PruneOlderThan(cutoff);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune old usage records.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
