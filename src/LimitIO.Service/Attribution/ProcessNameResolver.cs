using System.Collections.Concurrent;
using System.Diagnostics;

namespace LimitIO.Service.Attribution;

/// <summary>
/// Resolves a process ID to its executable name, with a short-lived cache. PIDs are reused by Windows
/// once a process exits, so entries expire quickly rather than being cached indefinitely - a long-lived
/// cache could otherwise attribute a new, unrelated process's traffic to whatever used to hold that PID.
/// </summary>
public sealed class ProcessNameResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    private sealed record CacheEntry(string? Name, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();

    public string? Resolve(int processId, DateTimeOffset now)
    {
        if (_cache.TryGetValue(processId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Name;
        }

        var name = LookupProcessName(processId);
        _cache[processId] = new CacheEntry(name, now + CacheLifetime);
        return name;
    }

    private static string? LookupProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // Process already exited between the connection-table snapshot and this lookup.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
