using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    private readonly ClientStateDbContext? _stateContext;

    /// <summary>Creates the composition of the connection with the event server.</summary>
    /// <param name="headers">
    /// Source of the identifiers of correlation that the requests carry. It carries the
    /// canonical identifier of the team, which the server REQUIRES in every request of the
    /// surface of the client (AD-15, AD-23).
    /// </param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="stateDatabasePath">
    /// Absolute path of the SQLite file of the client, or <c>null</c> when the client
    /// cannot open the database and therefore cannot read the ancla de confianza que ya
    /// está fijada (AD-27).
    /// </param>
    public ConnectionComposition(
        IClientRequestHeaders headers,
        ILoggerFactory loggerFactory,
        string? stateDatabasePath = null)
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

        // AD-27: la sonda del ancla de confianza lee la MISMA ruta que la de las versiones,
        // porque el servidor publica las dos cosas en ella. La sonda NO decide si el ancla
        // es la buena: solo devuelve lo que llegó (AD-27).
        var anchorProbe = new HttpTrustAnchorProbe(
            _httpClient,
            loggerFactory.CreateLogger<HttpTrustAnchorProbe>());

        // AD-27: el ancla fijada vive en la base de datos del cliente. Sin base de datos
        // el cliente no puede validar contra el valor PERSISTIDO, así que la conexión
        // queda en «Ancla sin verificar» (AC7, AD-27).
        ClientStateDbContext? stateContext = null;
        ITrustAnchorReader? anchorReader = null;

        if (!string.IsNullOrWhiteSpace(stateDatabasePath))
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = stateDatabasePath,
                Pooling = false,
            }.ToString();

            var options = new DbContextOptionsBuilder<ClientStateDbContext>()
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
                .Options;

            stateContext = new ClientStateDbContext(options);
            anchorReader = new ClientStateStore(stateContext);
        }

        _stateContext = stateContext;
        AnchorReader = anchorReader;
        StateStore = anchorReader as IClientStateStore;

        Service = new ServerDiscoveryService(
            _discovery,
            loggerFactory.CreateLogger<ServerDiscoveryService>());

        ConnectionService = new ServerConnectionService(
            _connector,
            versionProbe,
            versions,
            anchorReader,
            anchorProbe,
            loggerFactory.CreateLogger<ServerConnectionService>());

        AnchorProbe = anchorProbe;
    }

    /// <summary>The use case of the discovery of the event server.</summary>
    public ServerDiscoveryService Service { get; }

    /// <summary>The use case of the connection by the address that the player types.</summary>
    public ServerConnectionService ConnectionService { get; }

    /// <summary>
    /// The adapter that reads the ancla de confianza that the server publishes (AD-27).
    /// </summary>
    public IServerTrustAnchorProbe AnchorProbe { get; }

    /// <summary>
    /// The access to the ancla de confianza que ya está fijada, or <c>null</c> when the
    /// client could not open the database of state (AD-27).
    /// </summary>
    public ITrustAnchorReader? AnchorReader { get; }

    /// <summary>
    /// The access to the database of the client, or <c>null</c> when the client could not
    /// open it (AD-27).
    /// </summary>
    public IClientStateStore? StateStore { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();

        _stateContext?.Dispose();
    }
}
