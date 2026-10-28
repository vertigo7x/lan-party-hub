using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
namespace LANPartyHub.Server.Infrastructure.Networking;

/// <summary>
/// Source of the private addresses the server is allowed to listen on (AD-12).
/// It is a port so the composition root can be exercised with a host that has no private
/// address at all, which is the case the server has to refuse to start.
/// </summary>
public interface IPrivateAddressProvider
{
    /// <summary>Returns the private addresses of the operational adapters of the host.</summary>
    /// <returns>The private addresses, in the order the adapters report them.</returns>
    IReadOnlyList<IPAddress> GetPrivateAddresses();
}

/// <summary>
/// Resolves the private addresses of the host (AD-12).
///
/// The server listens only on private interfaces: the RFC 1918 ranges of IPv4 (10/8,
/// 172.16/12 and 192.168/16) and the unique local addresses of IPv6 (fc00::/7). The
/// loopback, the link local addresses, the tunnels and every public address are excluded,
/// because binding them would expose the hub outside the network of the event.
/// </summary>
public sealed class PrivateAddressResolver : IPrivateAddressProvider
{
    /// <summary>
    /// Tells whether an address belongs to a private range: RFC 1918 for IPv4 and the unique
    /// local addresses for IPv6. The loopback, the link local addresses and every public
    /// address are not private.
    /// </summary>
    /// <param name="address">Address to classify.</param>
    /// <returns><c>true</c> when the address belongs to a private range.</returns>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsPrivateIPv4(address);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !IPAddress.IsLoopback(address)
                && !address.IsIPv6LinkLocal
                && address.IsIPv6UniqueLocal;
        }

        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<IPAddress> GetPrivateAddresses()
    {
        var addresses = new List<IPAddress>();

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsOperational(adapter))
            {
                continue;
            }

            UnicastIPAddressInformationCollection unicastAddresses;

            try
            {
                unicastAddresses = adapter.GetIPProperties().UnicastAddresses;
            }
            catch (NetworkInformationException)
            {
                // The adapter cannot be read, most likely because it went down while it was
                // inspected. It is skipped instead of aborting the startup.
                continue;
            }

            foreach (var unicast in unicastAddresses)
            {
                if (IsPrivate(unicast.Address))
                {
                    addresses.Add(unicast.Address);
                }
            }
        }

        // Two adapters can report the same address, and Kestrel cannot listen twice in the
        // same endpoint.
        return addresses.Distinct().ToArray();
    }

    private static bool IsOperational(NetworkInterface adapter)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        return adapter.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Loopback => false,
            NetworkInterfaceType.Tunnel => false,
            NetworkInterfaceType.Ppp => false,
            _ => true,
        };
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            10 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            _ => false,
        };
    }
}
