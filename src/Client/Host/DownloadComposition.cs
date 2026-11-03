using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the downloads of the client (AD-4, AD-10, AD-14, AD-20, AD-24).
///
/// <para>
/// La composición entrega el puerto <see cref="IDownloadIntentClient"/> con SU ÚNICA
/// implementación, el adaptador que habla HTTP con el servidor del evento, y construye el
/// coordinador de las descargas con los límites de concurrencia que fija la configuración
/// efectiva (AD-14, AC10).
/// </para>
/// <para>
/// La composición es el ÚNICO sitio que conoce los adaptadores: el coordinador depende
/// de los puertos y nunca de ellos (AD-1).
/// </para>
/// </summary>
public sealed class DownloadComposition : IDisposable
{
    private readonly HttpClient _httpClient;

    /// <summary>Creates the composition of the downloads.</summary>
    /// <param name="serverAddress">
    /// Absolute address of the event server, which is the base of the surface of the
    /// client.
    /// </param>
    /// <param name="headers">
    /// Source of the identifiers of correlation that the requests carry. It carries the
    /// canonical identifier of the team, which the server REQUIRES in every request of the
    /// surface of the client (AD-15, AD-23).
    /// </param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="engine">
    /// Port of the transfer of bytes, delivered with the only implementation of the MVP
    /// (AD-4).
    /// </param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="maxConcurrentDownloads">
    /// Number of downloads that the client runs at the same time (AD-14).
    /// </param>
    /// <param name="maxTotalConnections">
    /// Number of connections that the client opens in total (AD-14).
    /// </param>
    public DownloadComposition(
        Uri serverAddress,
        IClientRequestHeaders headers,
        IClientStateStore stateStore,
        IClientDataLayout layout,
        ITransferEngine engine,
        ILoggerFactory loggerFactory,
        int maxConcurrentDownloads = DownloadCoordinator.DefaultMaxConcurrentDownloads,
        int maxTotalConnections = DownloadCoordinator.DefaultMaxTotalConnections)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // AD-6: la versión del cliente es la del binario que se está ejecutando y la
        // versión del protocolo es la constante del contrato compartido.
        var versions = new ClientBuildVersion();

        // La superficie del cliente EXIGE el identificador del equipo en cada petición,
        // así que el adaptador de la intención usa el cliente HTTP con el manejador de
        // cabeceras instalado (AD-15, AD-23).
        _httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, versions)
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = serverAddress,
        };

        Intents = new DownloadIntentHttpClient(_httpClient);

        Coordinator = new DownloadCoordinator(
            engine,
            Intents,
            stateStore,
            layout,
            loggerFactory.CreateLogger<DownloadCoordinator>(),
            maxConcurrentDownloads,
            maxTotalConnections);
    }

    /// <summary>
    /// Port of the registration of the intention of a download, delivered with its only
    /// implementation.
    /// </summary>
    public IDownloadIntentClient Intents { get; }

    /// <summary>
    /// The coordinator of the download operations of the client.
    /// </summary>
    public DownloadCoordinator Coordinator { get; }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
