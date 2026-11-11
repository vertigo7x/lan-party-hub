using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// The table of the sockets of the operating system, read for ONE process (AD-16).
///
/// <para>
/// La tabla de la MÁQUINA no sirve para atribuir un socket a un proceso: la máquina
/// ejecuta procesos de fondo que abren sockets de su cuenta, y una diferencia de la
/// tabla entera los reporta como si los hubiera abierto el servidor. La tabla con el
/// dueño de cada socket es la única fuente que los distingue.
/// </para>
/// <para>
/// El cliente y el servidor son productos de WINDOWS (AD-17), así que la fuente
/// natural es la tabla de Windows (<c>iphlpapi</c>). En un host que NO es Windows la
/// tabla equivalente es la del núcleo, en <c>/proc</c>: las entradas de
/// <c>/proc/net/udp</c> y <c>/proc/net/tcp</c> llevan el INODE del socket, y los
/// descriptores de <c>/proc/&lt;pid&gt;/fd</c> enlazan cada socket con su proceso.
/// Emparejar por inode da la MISMA atribución que la tabla de Windows, sin la
/// dependencia de una biblioteca que solo existe en una plataforma.
/// </para>
/// <para>
/// Se lee la tabla de IPv4, que es la que usa el servidor del evento, igual que hace
/// la tabla de Windows.
/// </para>
/// </summary>
internal static class ProcSocketTable
{
    /// <summary>
    /// A row of the table of the kernel.
    /// </summary>
    /// <param name="LocalEndPoint">Local endpoint of the socket.</param>
    /// <param name="RemoteEndPoint">Remote endpoint of the socket.</param>
    /// <param name="State">State of the socket, as the kernel numbers it.</param>
    internal sealed record Entry(IPEndPoint LocalEndPoint, IPEndPoint RemoteEndPoint, int State);

    /// <summary>
    /// The UDP sockets that belong to a process.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The UDP sockets of the process.</returns>
    public static IReadOnlyList<Entry> GetUdp(int processId) =>
        Read("/proc/net/udp", processId);

    /// <summary>
    /// The TCP connections that belong to a process.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The TCP connections of the process.</returns>
    public static IReadOnlyList<Entry> GetTcp(int processId) =>
        Read("/proc/net/tcp", processId);

    /// <summary>
    /// The INODES of the sockets that belong to a process, read from its descriptors.
    /// </summary>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The inodes of the sockets of the process.</returns>
    private static HashSet<long> SocketInodesOf(int processId)
    {
        var inodes = new HashSet<long>();

        var folder = $"/proc/{processId.ToString(CultureInfo.InvariantCulture)}/fd";

        if (!Directory.Exists(folder))
        {
            return inodes;
        }

        // La enumeración va PROTEGIDA: si el proceso muere mientras se leen sus
        // descriptores, la lista sale vacía en vez de romper la prueba con una
        // excepción (AD-16).
        string[] descriptors;

        try
        {
            descriptors = Directory.GetFileSystemEntries(folder);
        }
        catch (IOException)
        {
            return inodes;
        }
        catch (UnauthorizedAccessException)
        {
            return inodes;
        }

        foreach (var descriptor in descriptors)
        {
            string link;

            try
            {
                // El enlace de un socket es `socket:[<inode>]`, que no es una ruta del
                // sistema de archivos: se lee el enlace CRUDO, sin resolverlo.
                link = new FileInfo(descriptor).LinkTarget ?? string.Empty;
            }
            catch (IOException)
            {
                // El descriptor se cerró mientras se leía: el socket ya no cuenta.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            const string prefix = "socket:[";

            if (!link.StartsWith(prefix, StringComparison.Ordinal) || !link.EndsWith(']'))
            {
                continue;
            }

            if (long.TryParse(
                link[prefix.Length..^1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var inode))
            {
                inodes.Add(inode);
            }
        }

        return inodes;
    }

    /// <summary>
    /// Reads the table of the kernel and keeps the entries whose socket belongs to the
    /// process.
    /// </summary>
    /// <param name="tablePath">Path of the table of the kernel.</param>
    /// <param name="processId">Identifier of the process.</param>
    /// <returns>The entries of the table that belong to the process.</returns>
    private static IReadOnlyList<Entry> Read(string tablePath, int processId)
    {
        var entries = new List<Entry>();

        var inodes = SocketInodesOf(processId);

        if (inodes.Count == 0 || !File.Exists(tablePath))
        {
            return entries;
        }

        foreach (var line in File.ReadLines(tablePath).Skip(1))
        {
            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // sl local_address rem_address st tx:rx tr:when retrnsmt uid timeout inode
            if (columns.Length < 10)
            {
                continue;
            }

            if (!long.TryParse(
                columns[9],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var inode)
                || !inodes.Contains(inode))
            {
                continue;
            }

            var local = ParseEndPoint(columns[1]);
            var remote = ParseEndPoint(columns[2]);

            if (local is null || remote is null)
            {
                continue;
            }

            entries.Add(new Entry(local, remote, ParseState(columns[3])));
        }

        return entries;
    }

    /// <summary>
    /// Parses an endpoint of the table of the kernel: the address in hexadecimal and the
    /// port in hexadecimal, separated by a colon.
    /// </summary>
    /// <param name="column">Column of the table.</param>
    /// <returns>The endpoint, or <c>null</c> when the column is not one.</returns>
    private static IPEndPoint? ParseEndPoint(string column)
    {
        var separator = column.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0)
        {
            return null;
        }

        if (!uint.TryParse(
            column[..separator],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var address)
            || !int.TryParse(
            column[(separator + 1)..],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var port))
        {
            return null;
        }

        // La tabla imprime la dirección como el número que ocupa en memoria, y la
        // memoria la guarda en orden de RED: los bytes del número, en el orden del
        // host, son los bytes de la dirección.
        return new IPEndPoint(new IPAddress(BitConverter.GetBytes(address)), port);
    }

    /// <summary>
    /// Parses the state of a socket of the table of the kernel, which is a hexadecimal
    /// number.
    /// </summary>
    /// <param name="column">Column of the table.</param>
    /// <returns>The state, or zero when the column is not one.</returns>
    private static int ParseState(string column) =>
        int.TryParse(column, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var state)
            ? state
            : 0;

    /// <summary>
    /// Translates the state of the table of the kernel into the state of the sockets of
    /// .NET.
    /// </summary>
    /// <param name="state">State of the table of the kernel.</param>
    /// <returns>The state of the socket.</returns>
    public static TcpState TcpStateOf(int state) => state switch
    {
        1 => TcpState.Established,
        2 => TcpState.SynSent,
        3 => TcpState.SynReceived,
        4 => TcpState.FinWait1,
        5 => TcpState.FinWait2,
        6 => TcpState.TimeWait,
        7 => TcpState.Closed,
        8 => TcpState.CloseWait,
        9 => TcpState.LastAck,
        10 => TcpState.Listen,
        11 => TcpState.Closing,
        _ => TcpState.Unknown,
    };
}
