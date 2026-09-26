namespace LimitIO.Core.Models;

/// <summary>
/// A single time-limit rule: either a website (matched by domain suffix from the SNI/Host header)
/// or an application (matched by process name). Enforcement and accounting both key off <see cref="Id"/>,
/// not <see cref="Target"/>, so renaming a rule's target doesn't lose its usage history for the day.
/// </summary>
public sealed class LimitRule
{
    public required Guid Id { get; init; }

    public required LimitTargetType TargetType { get; set; }

    /// <summary>Domain (e.g. "instagram.com") or process name (e.g. "telegram.exe" / "telegram").</summary>
    public required string Target { get; set; }

    public required TimeSpan DailyBudget { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Local time-of-day at which this rule's usage and grace-used flag reset. Defaults to midnight.</summary>
    public TimeSpan ResetTime { get; set; } = TimeSpan.Zero;

    public static LimitRule Create(LimitTargetType type, string target, TimeSpan dailyBudget) => new()
    {
        Id = Guid.NewGuid(),
        TargetType = type,
        Target = NormalizeTarget(type, target),
        DailyBudget = dailyBudget,
    };

    /// <summary>
    /// Domains are lower-cased for stable suffix comparisons. Process names are lower-cased and have
    /// any ".exe" suffix stripped, since <see cref="ResolvedFlow.ProcessName"/> is compared the same way.
    /// </summary>
    public static string NormalizeTarget(LimitTargetType type, string target)
    {
        var trimmed = target.Trim().ToLowerInvariant();
        if (type == LimitTargetType.ProcessName && trimmed.EndsWith(".exe", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^4];
        }
        return trimmed;
    }

    /// <summary>Does this rule match the given observed hostname or process name?</summary>
    public bool Matches(string? domain, string? processName)
    {
        if (!Enabled)
        {
            return false;
        }

        return TargetType switch
        {
            LimitTargetType.Domain => domain is not null && DomainMatches(domain, Target),
            LimitTargetType.ProcessName => processName is not null &&
                string.Equals(NormalizeTarget(LimitTargetType.ProcessName, processName), Target, StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>True if <paramref name="observedHost"/> equals <paramref name="ruleTarget"/> or is a subdomain of it.</summary>
    public static bool DomainMatches(string observedHost, string ruleTarget)
    {
        var host = observedHost.Trim().ToLowerInvariant().TrimEnd('.');
        if (string.Equals(host, ruleTarget, StringComparison.Ordinal))
        {
            return true;
        }
        return host.EndsWith("." + ruleTarget, StringComparison.Ordinal);
    }
}
