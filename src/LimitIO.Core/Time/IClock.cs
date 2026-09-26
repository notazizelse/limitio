namespace LimitIO.Core.Time;

/// <summary>Testable indirection over wall-clock time so daily-reset and grace-expiry logic can be unit tested.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateTimeOffset LocalNow { get; }
}
