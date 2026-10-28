using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// A UDP socket of a process, read from the table of the operating system.
/// </summary>
/// <param name="LocalEndPoint">Local endpoint of the socket.</param>
internal sealed record ProcessUdpSocket(IPEndPoint LocalEndPoint);

/// <summary>
/// Reads the UDP sockets of a process from the table of the operating system.
/// <para>
/// The table of the whole machine cannot be used to attribute a socket to a process: the
/// machine runs background processes that open sockets of their own, and a diff of the whole
/// table reports them as if the server had opened them. The table with the owner of every
/// socket is the only source that tells them apart.
/// </para>
/// <para>It reads the IPv4 table, which is the one the server of the event uses.</para>
/// </summary>
internal static class ProcessUdpSockets
{
    private const int UdpTableOwnerPid = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr table,
        ref int size,
        bool order,
        AddressFamily family,
        int tableClass,
        int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint OwningProcess;
    }

    /// <summary>
    /// Returns the UDP sockets that belong to a process.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The UDP sockets of the process.</returns>
    public static IReadOnlyList<ProcessUdpSocket> Get(int processId)
    {
        var sockets = new List<ProcessUdpSocket>();

        var size = 0;

        _ = GetExtendedUdpTable(IntPtr.Zero, ref size, false, AddressFamily.InterNetwork, UdpTableOwnerPid, 0);

        if (size <= 0)
        {
            return sockets;
        }

        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (GetExtendedUdpTable(buffer, ref size, false, AddressFamily.InterNetwork, UdpTableOwnerPid, 0) != 0)
            {
                return sockets;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();

            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(buffer + sizeof(int) + (index * rowSize));

                if (row.OwningProcess != (uint)processId)
                {
                    continue;
                }

                sockets.Add(new ProcessUdpSocket(ToEndPoint(row.LocalAddress, row.LocalPort)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return sockets;
    }

    private static IPEndPoint ToEndPoint(uint address, uint port)
    {
        // The table holds the addresses and the ports in network byte order.
        var octets = BitConverter.GetBytes(address);

        return new IPEndPoint(new IPAddress(octets), (int)((port >> 8 & 0xFF) | (port << 8 & 0xFF00)));
    }
}
