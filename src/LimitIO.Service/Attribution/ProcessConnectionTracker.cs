using System.Net;
using System.Runtime.InteropServices;
using LimitIO.Core.Models;
using static LimitIO.Service.Attribution.NativeMethods;

namespace LimitIO.Service.Attribution;

/// <summary>
/// Maintains a snapshot mapping "local address/port" to owning process ID, refreshed periodically from
/// the IP Helper API. Covers both IPv4 and IPv6 (and both TCP and UDP) — an easy thing to only half do,
/// and one that would silently miss any IPv6-only app's traffic if skipped. This only identifies which
/// *local* endpoint owns a flow, which is exactly what the capture engine needs since every packet it
/// sees for a flow it should attribute has this machine as one side of the connection.
/// </summary>
public sealed class ProcessConnectionTracker
{
    private readonly record struct Key(bool IsTcp, IPAddress LocalAddress, ushort LocalPort);

    private Dictionary<Key, int> _snapshot = new();
    private readonly object _lock = new();

    public void Refresh()
    {
        var next = new Dictionary<Key, int>();

        AddTcpV4(next);
        AddTcpV6(next);
        AddUdpV4(next);
        AddUdpV6(next);

        lock (_lock)
        {
            _snapshot = next;
        }
    }

    public int? TryGetOwningProcessId(bool isTcp, IPAddress localAddress, ushort localPort)
    {
        var key = new Key(isTcp, Normalize(localAddress), localPort);
        lock (_lock)
        {
            return _snapshot.TryGetValue(key, out var pid) ? pid : null;
        }
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static void AddTcpV4(Dictionary<Key, int> table)
    {
        WithTable(AF_INET, TcpTableClass.TCP_TABLE_OWNER_PID_ALL, (buffer, rowCount) =>
        {
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            var rowsStart = buffer + 4;
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowsStart + i * rowSize);
                var addr = new IPAddress(row.localAddr);
                table[new Key(true, Normalize(addr), row.LocalPort)] = (int)row.owningPid;
            }
        });
    }

    private static void AddTcpV6(Dictionary<Key, int> table)
    {
        WithTable(AF_INET6, TcpTableClass.TCP_TABLE_OWNER_PID_ALL, (buffer, rowCount) =>
        {
            var rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
            var rowsStart = buffer + 4;
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowsStart + i * rowSize);
                var addr = new IPAddress(row.localAddr);
                table[new Key(true, Normalize(addr), row.LocalPort)] = (int)row.owningPid;
            }
        });
    }

    private static void AddUdpV4(Dictionary<Key, int> table)
    {
        WithUdpTable(AF_INET, (buffer, rowCount) =>
        {
            var rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
            var rowsStart = buffer + 4;
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowsStart + i * rowSize);
                var addr = new IPAddress(row.localAddr);
                table[new Key(false, Normalize(addr), row.LocalPort)] = (int)row.owningPid;
            }
        });
    }

    private static void AddUdpV6(Dictionary<Key, int> table)
    {
        WithUdpTable(AF_INET6, (buffer, rowCount) =>
        {
            var rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
            var rowsStart = buffer + 4;
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowsStart + i * rowSize);
                var addr = new IPAddress(row.localAddr);
                table[new Key(false, Normalize(addr), row.LocalPort)] = (int)row.owningPid;
            }
        });
    }

    private static void WithTable(int addressFamily, TcpTableClass tableClass, Action<IntPtr, int> readRows)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, sort: false, addressFamily, tableClass);
        if (size <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref size, sort: false, addressFamily, tableClass);
            if (result != NoError)
            {
                return;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            readRows(buffer, rowCount);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WithUdpTable(int addressFamily, Action<IntPtr, int> readRows)
    {
        var size = 0;
        _ = GetExtendedUdpTable(IntPtr.Zero, ref size, sort: false, addressFamily, UdpTableClass.UDP_TABLE_OWNER_PID);
        if (size <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedUdpTable(buffer, ref size, sort: false, addressFamily, UdpTableClass.UDP_TABLE_OWNER_PID);
            if (result != NoError)
            {
                return;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            readRows(buffer, rowCount);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
