using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Infrastructure.Networking;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the connection with the event server (AD-1, AD-3, FR-3).
///
/// <para>
/// The composition opens the adapter that speaks mDNS, the adapter that opens the socket
/// with the server and the use cases that search the servers and connect with the one
/// that the player chose. It is the ONLY place that knows the adapters: the use cases
/// depend on the ports declared in the Application layer and never on the
/// implementations (AD-1, AD-3).
/// </para>
/// <para>
/// The discovery is a convenience and NOT a requirement: the manual address is a
/// permanent and equivalent way to reach the server, so a failure of the composition does
/// not stop the client from starting (AC1, AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionComposition
{
    private readonly MdnsServerDiscovery _discovery;
    private readonly TcpServerConnector _connector;

    /// <summary>Creates the composition of the connection with the event server.</summary>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    public ConnectionComposition(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _discovery = new MdnsServerDiscovery(
            loggerFactory.CreateLogger<MdnsServerDiscovery>());

        _connector = new TcpServerConnector(
            loggerFactory.CreateLogger<TcpServerConnector>());

        Service = new ServerDiscoveryService(
            _discovery,
            loggerFactory.CreateLogger<ServerDiscoveryService>());

        ConnectionService = new ServerConnectionService(
            _connector,
            loggerFactory.CreateLogger<ServerConnectionService>());
    }

    /// <summary>The use case of the discovery of the event server.</summary>
    public ServerDiscoveryService Service { get; }

    /// <summary>The use case of the connection by the address that the player types.</summary>
    public ServerConnectionService ConnectionService { get; }
}
