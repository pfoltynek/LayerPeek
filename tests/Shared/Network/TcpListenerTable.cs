using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LayerPeek.Tests.Shared;

/// <summary>
/// TCP listening sockets of this machine from the OS table, without connecting (a refused connect to loopback takes
/// seconds on Windows). Shared by the proxy tests and the browser tests (T-227).
/// </summary>
/// <remarks>
/// On Windows the listeners are the rows of <c>GetExtendedTcpTable</c> (IPv4 and IPv6) without a remote end point
/// (0.0.0.0:0 / [::]:0), not the rows whose state says LISTEN: Windows 11 build 26300 (seen 2026-11-12) reports garbled
/// row states (most listening sockets as 0, some connections and TIME_WAIT sockets as LISTEN), so
/// <see cref="IPGlobalProperties.GetActiveTcpListeners"/>, <c>netstat</c> and <c>Get-NetTCPConnection</c> miss listening
/// sockets and report closed ones. A listening socket never has a remote end point and a connection always has one,
/// whatever the state field says. Other systems use <see cref="IPGlobalProperties.GetActiveTcpListeners"/>.
/// </remarks>
internal static unsafe partial class TcpListenerTable
{
    private const int AddressFamilyIPv4 = 2;
    private const int AddressFamilyIPv6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const uint ErrorInsufficientBuffer = 122;

    /// <summary>The local end points listening on <paramref name="port"/> (any address, IPv4 and IPv6).</summary>
    public static IPEndPoint[] Listeners(int port) =>
        OperatingSystem.IsWindows()
            ? [.. WindowsListeners(AddressFamilyIPv4, port), .. WindowsListeners(AddressFamilyIPv6, port)]
            : [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(e => e.Port == port)];

    /// <summary>Whether anything listens on <paramref name="port"/>.</summary>
    public static bool IsListening(int port) => Listeners(port).Length != 0;

    private static List<IPEndPoint> WindowsListeners(int addressFamily, int port)
    {
        // MIB_TCPROW_OWNER_PID: state, local address, local port, remote address, remote port, pid (6 DWORDs).
        // MIB_TCP6ROW_OWNER_PID: local address[16], scope, local port, remote address[16], scope, remote port, state, pid.
        bool v6 = addressFamily == AddressFamilyIPv6;
        int rowSize = v6 ? 56 : 24;
        int localAddressOffset = v6 ? 0 : 4;
        int localPortOffset = v6 ? 20 : 8;
        int remoteAddressOffset = v6 ? 24 : 12;
        int remotePortOffset = v6 ? 44 : 16;
        int addressLength = v6 ? 16 : 4;

        int size = 0;
        byte[] table = [];
        uint result = ErrorInsufficientBuffer;
        // The table can grow between the size query and the read; a few attempts are enough.
        for (int attempt = 0; attempt < 5 && result == ErrorInsufficientBuffer; attempt++)
        {
            table = new byte[Math.Max(size, 4)];
            fixed (byte* buffer = table)
            {
                result = GetExtendedTcpTable(buffer, ref size, 0, addressFamily, TcpTableOwnerPidAll, 0);
            }
        }

        if (result != 0)
        {
            throw new InvalidOperationException($"GetExtendedTcpTable failed with {result}.");
        }

        List<IPEndPoint> listeners = [];
        int count = BitConverter.ToInt32(table, 0);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> row = table.AsSpan(4 + (i * rowSize), rowSize);
            int localPort = PortOf(row[localPortOffset..]);
            if (localPort != port || PortOf(row[remotePortOffset..]) != 0 || row.Slice(remoteAddressOffset, addressLength).ContainsAnyExcept((byte)0))
            {
                continue;
            }

            // IPv6 rows carry the scope of the local address (link-local listeners), like GetActiveTcpListeners reports it.
            IPAddress address = v6
                ? new IPAddress(row.Slice(localAddressOffset, addressLength), BitConverter.ToUInt32(row[16..]))
                : new IPAddress(row.Slice(localAddressOffset, addressLength));
            listeners.Add(new IPEndPoint(address, localPort));
        }

        return listeners;
    }

    /// <summary>A port DWORD of the TCP table: the port in network byte order in its first two bytes.</summary>
    private static int PortOf(ReadOnlySpan<byte> value) => (value[0] << 8) | value[1];

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(byte* tcpTable, ref int size, int order, int addressFamily, int tableClass, uint reserved);
}
