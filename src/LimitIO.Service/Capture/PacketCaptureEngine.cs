using System.Net;
using LimitIO.Core.Models;
using LimitIO.Service.Attribution;
using Microsoft.Extensions.Logging;
using WinDivertSharp;
using WinDivertSharp.Extensions;

namespace LimitIO.Service.Capture;

/// <summary>
/// Owns the WinDivert packet-capture handle and the main recv/decide/reinject-or-drop loop. This is the
/// enforcement point: a flow that <see cref="BlockDecider"/> says to block simply never gets reinjected,
/// which is what makes it invisible to the network stack from that point on (see docs/ARCHITECTURE.md for
/// why this looks like a stall/timeout to the blocked app rather than a friendly error).
/// </summary>
public sealed class PacketCaptureEngine : IDisposable
{
    // Narrowed to outbound TCP:80/443 (where we can read a hostname) plus all outbound UDP (so
    // process-level rules still catch UDP-heavy apps like Telegram calls or Discord, even though we
    // can't attribute a hostname to UDP traffic). Inbound traffic is intentionally not captured - only
    // the outbound leg needs to be seen (and possibly dropped) to enforce a limit.
    private const string Filter = "outbound and (tcp.DstPort == 80 or tcp.DstPort == 443 or udp)";
    private const short Priority = 0;
    private const int BufferSize = 65535;
    private static readonly TimeSpan ConnectionTableRefreshInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ReassemblySweepInterval = TimeSpan.FromSeconds(10);

    private readonly ProcessConnectionTracker _connectionTracker = new();
    private readonly ProcessNameResolver _processNameResolver = new();
    private readonly FlowReassemblyTracker _reassembly = new();
    private readonly ILogger<PacketCaptureEngine> _logger;

    private IntPtr _handle = IntPtr.Zero;

    public IFlowBlockDecider? BlockDecider { get; set; }

    public event Action<ResolvedFlow>? FlowObserved;

    public PacketCaptureEngine(ILogger<PacketCaptureEngine> logger)
    {
        _logger = logger;
    }

    public void Open()
    {
        _handle = WinDivert.WinDivertOpen(Filter, WinDivertLayer.Network, Priority, WinDivertOpenFlags.None);
        if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"WinDivertOpen failed (Win32 error {error}). This usually means the service isn't running elevated, " +
                "the WinDivert64.sys driver failed to load, or another instance already holds a conflicting handle.");
        }

        _logger.LogInformation("WinDivert capture handle opened.");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var backgroundTasks = new[]
        {
            Task.Run(() => RefreshConnectionTableLoopAsync(ct), ct),
            Task.Run(() => SweepReassemblyLoopAsync(ct), ct),
        };

        using var packet = new WinDivertBuffer(BufferSize);

        while (!ct.IsCancellationRequested)
        {
            var address = new WinDivertAddress();
            uint readLen = 0;
            bool received;
            try
            {
                received = WinDivert.WinDivertRecv(_handle, packet, ref address, ref readLen);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WinDivertRecv threw unexpectedly; backing off briefly.");
                await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }

            if (!received)
            {
                // Transient recv failure (e.g. buffer too small for a jumbo packet) - drop this one packet
                // and keep going rather than tearing down the whole capture loop over it.
                continue;
            }

            try
            {
                HandlePacket(packet, readLen, ref address);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process a captured packet; reinjecting it unmodified to fail open.");
                TryReinject(packet, readLen, ref address);
            }
        }

        await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
    }

    private void HandlePacket(WinDivertBuffer packet, uint readLen, ref WinDivertAddress address)
    {
        IPv4Header? ipv4 = null;
        IPv6Header? ipv6 = null;
        IcmpV4Header? icmp4 = null;
        IcmpV6Header? icmp6 = null;
        TcpHeader? tcp = null;
        UdpHeader? udp = null;
        Span<byte> data = default;

        var parsed = WinDivert.WinDivertHelperParsePacket(
            packet, readLen, ref ipv4, ref ipv6, ref icmp4, ref icmp6, ref tcp, ref udp, ref data);

        if (!parsed)
        {
            TryReinject(packet, readLen, ref address);
            return;
        }

        var localAddress = ipv4?.SrcAddr ?? ipv6?.SrcAddr;
        var remoteAddress = ipv4?.DstAddr ?? ipv6?.DstAddr;
        var isTcp = tcp is not null;

        if (localAddress is null || remoteAddress is null || (tcp is null && udp is null))
        {
            // ICMP or something else our filter shouldn't have let through - pass it on unexamined.
            TryReinject(packet, readLen, ref address);
            return;
        }

        var localPort = isTcp ? tcp!.Value.SrcPort.SwapByteOrder() : udp!.Value.SrcPort.SwapByteOrder();
        var remotePort = isTcp ? tcp!.Value.DstPort.SwapByteOrder() : udp!.Value.DstPort.SwapByteOrder();

        var flowKey = new FlowKey(isTcp, localAddress, localPort, remoteAddress, remotePort);
        var now = DateTimeOffset.UtcNow;

        var processId = _connectionTracker.TryGetOwningProcessId(isTcp, localAddress, localPort);
        var processName = processId.HasValue ? _processNameResolver.Resolve(processId.Value, now) : null;

        string? domain = null;
        if (isTcp && data.Length > 0 && (remotePort == 443 || remotePort == 80))
        {
            domain = ExtractHostname(flowKey, remotePort, data, now);
        }

        FlowObserved?.Invoke(new ResolvedFlow(flowKey, processId ?? 0, processName, domain, now));

        var shouldBlock = BlockDecider?.ShouldBlock(flowKey, processId ?? 0, processName, domain) ?? false;
        if (shouldBlock)
        {
            // Not reinjecting *is* the block - the packet simply vanishes from the network stack's
            // perspective, which is why a freshly-blocked app appears to hang/time out rather than
            // getting a clean error (see docs/ARCHITECTURE.md, "when a limit hits mid-session").
            return;
        }

        TryReinject(packet, readLen, ref address);
    }

    private string? ExtractHostname(FlowKey flowKey, ushort remotePort, Span<byte> data, DateTimeOffset now)
    {
        // Bounded, best-effort reassembly: a ClientHello or HTTP request line split across TCP segments
        // gets accumulated here until it parses or the flow's buffer cap is hit (see
        // FlowReassemblyTracker remarks - once capped, we still re-scan the capped buffer on every
        // subsequent packet, which is a little wasteful but bounded and far simpler than tracking a
        // separate "gave up on this flow" set).
        var buffered = _reassembly.Append(flowKey, data, now);

        string? result = remotePort == 443
            ? TlsClientHelloParser.TryParseServerName(buffered.Span, out var sniName) ? sniName : null
            : HttpHostHeaderParser.TryParseHost(buffered.Span, out var hostName) ? hostName : null;

        if (result is not null || _reassembly.IsCapped(flowKey))
        {
            _reassembly.Forget(flowKey);
        }

        return result;
    }

    private void TryReinject(WinDivertBuffer packet, uint readLen, ref WinDivertAddress address)
    {
        try
        {
            WinDivert.WinDivertSend(_handle, packet, readLen, ref address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reinject an allowed packet.");
        }
    }

    private async Task RefreshConnectionTableLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connectionTracker.Refresh();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh the TCP/UDP connection table.");
            }

            try
            {
                await Task.Delay(ConnectionTableRefreshInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task SweepReassemblyLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _reassembly.Sweep(DateTimeOffset.UtcNow);
                await Task.Delay(ReassemblySweepInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            WinDivert.WinDivertClose(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
