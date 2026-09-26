using System.Runtime.InteropServices;

namespace LimitIO.Service.Attribution;

/// <summary>
/// Raw P/Invoke declarations for the IP Helper API (iphlpapi.dll) tables that map an active TCP/UDP
/// connection back to the owning process ID. This is the standard, well-documented pattern used by
/// "netstat -b"-style tools (GetExtendedTcpTable/GetExtendedUdpTable with the *_OWNER_PID table class) -
/// no elevated hooking or kernel driver is needed for this half of attribution, only for the packet
/// capture itself (WinDivert).
/// </summary>
internal static class NativeMethods
{
    internal const int AF_INET = 2;
    internal const int AF_INET6 = 23;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int NoError = 0;

    internal enum TcpTableClass
    {
        TCP_TABLE_BASIC_LISTENER,
        TCP_TABLE_BASIC_CONNECTIONS,
        TCP_TABLE_BASIC_ALL,
        TCP_TABLE_OWNER_PID_LISTENER,
        TCP_TABLE_OWNER_PID_CONNECTIONS,
        TCP_TABLE_OWNER_PID_ALL,
        TCP_TABLE_OWNER_MODULE_LISTENER,
        TCP_TABLE_OWNER_MODULE_CONNECTIONS,
        TCP_TABLE_OWNER_MODULE_ALL,
    }

    internal enum UdpTableClass
    {
        UDP_TABLE_BASIC,
        UDP_TABLE_OWNER_PID,
        UDP_TABLE_OWNER_MODULE,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public byte localPort1, localPort2, localPort3, localPort4;
        public uint remoteAddr;
        public byte remotePort1, remotePort2, remotePort3, remotePort4;
        public uint owningPid;

        public readonly ushort LocalPort => (ushort)((localPort1 << 8) | localPort2);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public byte localPort1, localPort2, localPort3, localPort4;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] remoteAddr;
        public uint remoteScopeId;
        public byte remotePort1, remotePort2, remotePort3, remotePort4;
        public uint state;
        public uint owningPid;

        public readonly ushort LocalPort => (ushort)((localPort1 << 8) | localPort2);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public byte localPort1, localPort2, localPort3, localPort4;
        public uint owningPid;

        public readonly ushort LocalPort => (ushort)((localPort1 << 8) | localPort2);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public byte localPort1, localPort2, localPort3, localPort4;
        public uint owningPid;

        public readonly ushort LocalPort => (ushort)((localPort1 << 8) | localPort2);
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ulAf, TcpTableClass tableClass, uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ulAf, UdpTableClass tableClass, uint reserved = 0);
}
