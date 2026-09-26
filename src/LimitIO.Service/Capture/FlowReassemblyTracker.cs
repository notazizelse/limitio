using System.Collections.Concurrent;
using LimitIO.Core.Models;

namespace LimitIO.Service.Capture;

/// <summary>
/// Accumulates the first bytes of each TCP flow's payload across possibly-multiple packets, so a
/// ClientHello (or HTTP request line) that arrives split across TCP segments can still be parsed. Bounded
/// per flow so a malicious or huge handshake can't grow memory unbounded, and time-boxed so abandoned
/// entries eventually get swept.
/// </summary>
public sealed class FlowReassemblyTracker
{
    private const int MaxBufferedBytesPerFlow = 8192;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromSeconds(15);

    private sealed class Entry
    {
        public readonly MemoryStream Buffer = new();
        public DateTimeOffset LastSeen;
    }

    private readonly ConcurrentDictionary<FlowKey, Entry> _flows = new();

    /// <summary>
    /// Appends new payload bytes for the flow and returns everything buffered for it so far. Once a
    /// caller successfully parses a hostname (or gives up), it should call <see cref="Forget"/> so the
    /// entry doesn't linger until <see cref="Sweep"/> catches it.
    /// </summary>
    public ReadOnlyMemory<byte> Append(FlowKey key, ReadOnlySpan<byte> newBytes, DateTimeOffset now)
    {
        var entry = _flows.GetOrAdd(key, _ => new Entry());
        entry.LastSeen = now;

        lock (entry.Buffer)
        {
            if (entry.Buffer.Length < MaxBufferedBytesPerFlow)
            {
                var remaining = MaxBufferedBytesPerFlow - (int)entry.Buffer.Length;
                var toWrite = newBytes.Length > remaining ? newBytes[..remaining] : newBytes;
                entry.Buffer.Write(toWrite);
            }
            return entry.Buffer.ToArray();
        }
    }

    public void Forget(FlowKey key) => _flows.TryRemove(key, out _);

    public bool IsCapped(FlowKey key) =>
        _flows.TryGetValue(key, out var entry) && entry.Buffer.Length >= MaxBufferedBytesPerFlow;

    /// <summary>Drops entries that haven't seen a packet in <see cref="EntryLifetime"/>, to bound memory over time.</summary>
    public void Sweep(DateTimeOffset now)
    {
        foreach (var (key, entry) in _flows)
        {
            if (now - entry.LastSeen > EntryLifetime)
            {
                _flows.TryRemove(key, out _);
            }
        }
    }

    public int TrackedFlowCount => _flows.Count;
}
