using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure;
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
/// AD-6, FR-4: la composición abre también el sondeo de las versiones del servidor y la
/// versión del cliente, de modo que la conexión comprueba la compatibilidad ANTES de dar
/// la conexión por buena.
/// </para>
/// <para>
/// The discovery is a convenience and NOT a requirement: the manual address is a
/// permanent and equivalent way to reach the server, so a failure of the composition does
/// not stop the client from starting (AC1, AC4, AD-3).
/// </para>
/// </summary>
public sealed class ConnectionComposition : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly MdnsServerDiscovery _discovery;
    private readonly TcpServerConnector _connector;

    /// <summary>Creates the composition of the connection with the event server.</summary>
    /// <param name="headers">
    /// Source of the identifiers of correlation that the requests carry. It carries the
    /// canonical identifier of the team, which the server REQUIRES in every request of the
    /// surface of the client (AD-15, AD-23).
    /// </param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    public ConnectionComposition(IClientRequestHeaders headers, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _discovery = new MdnsServerDiscovery(
            loggerFactory.CreateLogger<MdnsServerDiscovery>());

        _connector = new TcpServerConnector(
            loggerFactory.CreateLogger<TcpServerConnector>());

        // AD-6: la versión del cliente es la del binario que se está ejecutando y la
        // versión del protocolo es la constante del contrato compartido.
        var versions = new ClientBuildVersion();

        // AD-6, AD-15, AD-23: el sondeo habla HTTP con el servidor. El manejador de
        // cabeceras estampa la versión del cliente, la del protocolo y el identificador
        // del equipo en TODA petición, así que el servidor puede decidir la
        // compatibilidad (AD-6, AD-15).
        //
        // El identificador del cliente es OBLIGATORIO: la ruta del sondeo es de la
        // superficie del cliente y el servidor rechaza con 400 la petición que no lo
        // lleva (AD-23). Por eso la sonda usa los identificadores reales y no unos de
        // mentira: sin ellos el sondeo SIEMPRE recibiría un 400 y nunca podría pronunciarse
        // sobre la compatibilidad.
        _httpClient = new HttpClient(
            new ClientRequestHeadersHandler(
                headers,
                versions)
            {
                InnerHandler = new HttpClientHandler(),
            });

        var versionProbe = new HttpServerVersionProbe(
            _httpClient,
            loggerFactory.CreateLogger<HttpServerVersionProbe>());

        Service = new ServerDiscoveryService(
            _discovery,
            loggerFactory.CreateLogger<ServerDiscoveryService>());

        ConnectionService = new ServerConnectionService(
            _connector,
            versionProbe,
            versions,
            loggerFactory.CreateLogger<ServerConnectionService>());
    }

    /// <summary>The use case of the discovery of the event server.</summary>
    public ServerDiscoveryService Service { get; }

    /// <summary>The use case of the connection by the address that the player types.</summary>
    public ServerConnectionService ConnectionService { get; }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
