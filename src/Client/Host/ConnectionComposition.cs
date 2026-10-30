using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Infrastructure.Networking;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the discovery of the event server (AD-1, AD-3, FR-3).
///
/// <para>
/// The composition opens the adapter that speaks mDNS and the use case that searches the
/// servers with a time limit. It is the ONLY place that knows the adapter: the use case
/// depends on the port declared in the Application layer and never on the implementation
/// (AD-1, AD-3).
/// </para>
/// <para>
/// The discovery is a convenience and NOT a requirement: the manual address is a
/// permanent and equivalent way to reach the server, so a failure of the composition does
/// not stop the client from starting (AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionComposition
{
    private readonly MdnsServerDiscovery _discovery;

    /// <summary>Creates the composition of the discovery of the event server.</summary>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    public ConnectionComposition(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _discovery = new MdnsServerDiscovery(
            loggerFactory.CreateLogger<MdnsServerDiscovery>());

        Service = new ServerDiscoveryService(
            _discovery,
            loggerFactory.CreateLogger<ServerDiscoveryService>());
    }

    /// <summary>The use case of the discovery of the event server.</summary>
    public ServerDiscoveryService Service { get; }
}
