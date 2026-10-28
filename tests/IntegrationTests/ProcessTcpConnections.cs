using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// A TCP connection of a process, read from the table of the operating system.
/// </summary>
/// <param name="LocalEndPoint">Local endpoint of the connection.</param>
/// <param name="RemoteEndPoint">Remote endpoint of the connection.</param>
/// <param name="State">State of the connection.</param>
internal sealed record ProcessTcpConnection(
    IPEndPoint LocalEndPoint,
    IPEndPoint RemoteEndPoint,
    TcpState State);

/// <summary>
/// Reads the TCP connections of a process from the table of the operating system.
/// <para>
/// The table of the whole machine cannot be used to attribute a connection to a process: the
/// machine runs background processes that open connections of their own, and a diff of the
/// whole table reports them as if the server had opened them. The table with the owner of
/// every connection is the only source that tells them apart.
/// </para>
/// <para>It reads the IPv4 table, which is the one the server of the event uses.</para>
/// </summary>
internal static class ProcessTcpConnections
{
    private const int TcpTableOwnerPidAll = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool order,
        AddressFamily family,
        int tableClass,
        int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningProcess;
    }

    /// <summary>
    /// Returns the TCP connections that belong to a process.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The connections of the process.</returns>
    public static IReadOnlyList<ProcessTcpConnection> Get(int processId)    {
        var connections = new List<ProcessTcpConnection>();

        var size = 0;

        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AddressFamily.InterNetwork, TcpTableOwnerPidAll, 0);

        if (size <= 0)
        {
            return connections;
        }

        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AddressFamily.InterNetwork, TcpTableOwnerPidAll, 0) != 0)
            {
                return connections;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buffer + sizeof(int) + (index * rowSize));

                if (row.OwningProcess != (uint)processId)
                {
                    continue;
                }

                connections.Add(new ProcessTcpConnection(
                    ToEndPoint(row.LocalAddress, row.LocalPort),
                    ToEndPoint(row.RemoteAddress, row.RemotePort),
                    (TcpState)row.State));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return connections;
    }

    /// <summary>
    /// Returns the sockets that a process has in listening state.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The listening sockets of the process.</returns>
    public static IReadOnlyList<ProcessTcpConnection> GetListening(int processId) =>
        Get(processId)
            .Where(connection => connection.State == TcpState.Listen)
            .ToArray();

    /// <summary>
    /// Returns the connections that a process has, without the sockets that it only listens
    /// with.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The connections of the process.</returns>
    public static IReadOnlyList<ProcessTcpConnection> GetConnected(int processId) =>
        Get(processId)
            .Where(connection => connection.State != TcpState.Listen)
            .ToArray();

    private static IPEndPoint ToEndPoint(uint address, uint port)
    {
        // The table holds the addresses and the ports in network byte order.
        var octets = BitConverter.GetBytes(address);

        return new IPEndPoint(new IPAddress(octets), (int)((port >> 8 & 0xFF) | (port << 8 & 0xFF00)));
    }
}
