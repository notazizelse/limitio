using LimitIO.Core.Models;
using LimitIO.Core.Storage;

namespace LimitIO.Service.Configuration;

/// <summary>
/// In-memory, thread-safe view of the persisted <see cref="AppConfig"/>. Packet processing happens on
/// the capture loop's hot path (potentially thousands of times per second under heavy traffic) and must
/// never hit disk per-packet to check the current rule set - this cache is the single in-memory source
/// of truth that both the capture/enforcement path (readers) and IPC request handling (the only writer)
/// go through, so the two can never see inconsistent state.
/// </summary>
public sealed class ConfigCache
{
    private readonly ConfigStore _store;
    private readonly object _lock = new();
    private AppConfig _current;

    public ConfigCache(ConfigStore store)
    {
        _store = store;
        _current = store.Load();
    }

    /// <summary>A snapshot of the current config. Safe to read from any thread without further locking.</summary>
    public AppConfig Current
    {
        get { lock (_lock) { return _current; } }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _current = _store.Load();
        }
    }

    /// <summary>Applies a mutation to the in-memory config and persists the result, atomically with respect to other writers/readers.</summary>
    public void Update(Action<AppConfig> mutate)
    {
        lock (_lock)
        {
            mutate(_current);
            _store.Save(_current);
        }
    }
}
