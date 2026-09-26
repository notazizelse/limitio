namespace LimitIO.Core.Models;

/// <summary>The 5-tuple identifying a single network flow, used as a dictionary key for in-memory tracking.</summary>
public readonly record struct FlowKey(
    bool IsTcp,
    System.Net.IPAddress LocalAddress,
    ushort LocalPort,
    System.Net.IPAddress RemoteAddress,
    ushort RemotePort);

/// <summary>
/// What the capture/attribution pipeline learned about one flow: which process owns it, and (best-effort)
/// which hostname it's talking to. <see cref="Domain"/> is null when SNI/Host extraction failed (e.g. an
/// Encrypted Client Hello connection, plain non-HTTP traffic, or a ClientHello split across more segments
/// than the parser buffers) — see docs/ARCHITECTURE.md for how enforcement falls back in that case.
/// </summary>
public sealed record ResolvedFlow(FlowKey Key, int ProcessId, string? ProcessName, string? Domain, DateTimeOffset ObservedAt);
