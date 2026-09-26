namespace LimitIO.Core.Models;

/// <summary>One rule's accumulated usage for one calendar day (in that rule's own local reset-day sense).</summary>
public sealed class UsageRecord
{
    public required Guid RuleId { get; init; }

    public required DateOnly Date { get; init; }

    public TimeSpan Consumed { get; set; } = TimeSpan.Zero;

    /// <summary>Whether the once-per-day "ignore for 1 minute" grace has already been used for this rule today.</summary>
    public bool GraceUsedToday { get; set; }
}
