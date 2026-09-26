using System.Collections.Concurrent;
using LimitIO.Core.Time;

namespace LimitIO.Service.Ipc;

/// <summary>
/// Issues and validates the short-lived session tokens that gate any rule-mutating IPC request. The UI is
/// never trusted to "decide" it's authenticated - it must present a token this class itself issued after
/// a successful password check, and that token can expire or be revoked out from under it. Also tracks a
/// simple global exponential backoff on failed password attempts, enforced here (server-side) rather than
/// only in the UI's prompt window, since a modified client could otherwise hammer the pipe directly.
/// </summary>
public sealed class AuthSessionManager
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();
    private readonly IClock _clock;
    private readonly object _failureLock = new();
    private int _consecutiveFailures;
    private DateTimeOffset _lastFailureAt;

    public AuthSessionManager(IClock clock)
    {
        _clock = clock;
    }

    public string IssueSession()
    {
        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = _clock.UtcNow + SessionLifetime;
        lock (_failureLock)
        {
            _consecutiveFailures = 0;
        }
        return token;
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (_sessions.TryGetValue(token, out var expiry) && expiry > _clock.UtcNow)
        {
            _sessions[token] = _clock.UtcNow + SessionLifetime; // sliding expiry while actively used
            return true;
        }

        _sessions.TryRemove(token, out _);
        return false;
    }

    public void Revoke(string token) => _sessions.TryRemove(token, out _);

    public void RevokeAll() => _sessions.Clear();

    /// <summary>Returns how much longer the caller must wait before another VerifyPassword attempt is accepted, or null if none.</summary>
    public TimeSpan? GetRequiredBackoff()
    {
        lock (_failureLock)
        {
            if (_consecutiveFailures == 0)
            {
                return null;
            }

            var delay = ComputeDelay(_consecutiveFailures);
            var elapsed = _clock.UtcNow - _lastFailureAt;
            return elapsed < delay ? delay - elapsed : null;
        }
    }

    public void RecordFailure()
    {
        lock (_failureLock)
        {
            _consecutiveFailures++;
            _lastFailureAt = _clock.UtcNow;
        }
    }

    private static TimeSpan ComputeDelay(int consecutiveFailures)
    {
        // 2s, 4s, 8s, 16s, 32s, capped at 60s.
        var seconds = Math.Min(2 * Math.Pow(2, Math.Max(0, consecutiveFailures - 1)), MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
