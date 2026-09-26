namespace LimitIO.Core.Models;

/// <summary>
/// The Service's persisted configuration: password credentials and the rule set. Owned exclusively by
/// the Service (see docs/ARCHITECTURE.md "who owns the truth") — the UI never reads or writes this file
/// directly, only through IPC, so a restricted user finding the file on disk gains nothing.
/// </summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public byte[]? PasswordHash { get; set; }

    public byte[]? PasswordSalt { get; set; }

    public int PasswordIterations { get; set; }

    public bool PasswordConfigured => PasswordHash is not null && PasswordSalt is not null;

    public List<LimitRule> Rules { get; set; } = [];

    /// <summary>When set and in the future, all enforcement is suspended until this instant (the password-gated "pause" feature).</summary>
    public DateTimeOffset? MonitoringPausedUntil { get; set; }
}
