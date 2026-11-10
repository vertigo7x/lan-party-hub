using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Clients;

/// <summary>
/// Use case that assembles the REAL state of the client and publishes it to the server
/// (AD-18, AD-22, FR-10).
///
/// <para>
/// El estado que el cliente publica es el REAL: las instalaciones que tiene
/// registradas, con su versión y su estado de máquina, y las operaciones que están en
/// curso, con su estado y su tipo (AD-18, AD-22).
/// </para>
/// <para>
/// Las instalaciones son LOCALES: el cliente lee su propia base de datos y NUNCA pide
/// al servidor nada sobre ellas. La ruta de la instalación no sale del cliente, así que
/// el payload no la lleva (AD-22).
/// </para>
/// <para>
/// La publicación NUNCA impide que el jugador siga usando el cliente: el estado es un
/// complemento y no su motivo de vida (AD-1).
/// </para>
/// </summary>
public sealed class PublishClientState
{
    private readonly IClientStateStore _stateStore;
    private readonly IClientStatePublisher _publisher;
    private readonly ClientIdentityProvider _identity;
    private readonly IActiveInstallationSource? _activeInstallations;

    /// <summary>Creates the use case that publishes the state of the client.</summary>
    /// <param name="stateStore">Access to the local database of the client.</param>
    /// <param name="publisher">Publisher of the state of the client.</param>
    /// <param name="identity">Provider of the canonical identifier of the client.</param>
    /// <param name="activeInstallations">
    /// Source of the INSTALLATION operations that the client is running right now, or
    /// <c>null</c> when the client does not track them. Sin fuente, las instalaciones en
    /// curso no se publican, y la publicación del resto del estado no se rompe (AD-1,
    /// AD-22).
    /// </param>
    public PublishClientState(
        IClientStateStore stateStore,
        IClientStatePublisher publisher,
        ClientIdentityProvider identity,
        IActiveInstallationSource? activeInstallations = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(identity);

        _stateStore = stateStore;
        _publisher = publisher;
        _identity = identity;
        _activeInstallations = activeInstallations;
    }

    /// <summary>
    /// Assembles the REAL state of the client and publishes it (AD-18, AD-22).
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task ExecuteAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        var clientId = _identity.CanonicalId;

        // AD-22: las instalaciones son LOCALES y su ruta NUNCA sale del cliente.
        var installations = await _stateStore
            .LoadInstallationsAsync(cancellationToken)
            .ConfigureAwait(false);

        // AD-10, AD-22: las operaciones en curso son las que NO están en un estado
        // terminal. Una operación terminada no se publica como activa.
        var operations = await _stateStore
            .LoadDownloadOperationsAsync(cancellationToken)
            .ConfigureAwait(false);

        // AD-22: las INSTALACIONES en curso son operaciones en curso como las demás, así
        // que la tabla del organizador muestra la fase REAL de la que está instalando.
        var activeInstallations = _activeInstallations?.ListActiveOperations() ?? [];

        var state = new ClientStateChanged(
            clientId,
            connectionState,
            [
                .. installations.Select(installation => new ClientStateChanged.InstalledGame(
                    installation.GameId,
                    installation.InstalledVersion,
                    installation.ValidationState))
            ],
            [
                .. operations
                    .Where(operation => !IsTerminal(operation.State))
                    .Select(operation => new ClientStateChanged.ActiveOperation(
                        operation.OperationId,
                        DownloadOperationTypeName,
                        operation.State)),
                .. activeInstallations,
            ]);

        await _publisher
            .PublishAsync(state, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Name of the kind of operation of a download, que es el nombre estable del valor y
    /// no un texto libre (AD-22, R-MVP-7).
    /// </summary>
    public const string DownloadOperationTypeName = OperationTypeNames.Download;

    /// <summary>
    /// Tells whether the state of an operation is TERMINAL, y NO se publica como
    /// operación en curso (AD-10).
    /// </summary>
    /// <param name="state">Name of the state of the operation.</param>
    /// <returns><c>true</c> when the state is terminal.</returns>
    private static bool IsTerminal(string state)
    {
        if (!Enum.TryParse<DownloadState>(state, ignoreCase: false, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            // Un estado que la máquina NO reconoce no se inventa: la operación se
            // publica como activa, que es lo que el servidor puede mostrar (AD-10).
            return false;
        }

        return parsed is DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed;
    }
}

/// <summary>
/// Source of the canonical identifier of the client (AD-23).
///
/// <para>
/// El identificador lo emitió el cliente una sola vez y lo persistió, así que la
/// composición lo lee de su propia base de datos y lo entrega a los casos de uso que
/// necesitan publicarlo (AD-23).
/// </para>
/// </summary>
public interface ClientIdentityProvider
{
    /// <summary>Canonical identifier of the client.</summary>
    string CanonicalId { get; }
}

/// <summary>
/// Source of the INSTALLATION operations that the client is running right now
/// (AD-18, AD-22, FR-10).
///
/// <para>
/// El port está declarado en la capa de Application y lo implementa el tracker que
/// lleva la cuenta de las instalaciones en curso. El caso de uso de la publicación
/// depende del port y nunca del tracker, así que la forma del evento se puede
/// ejercitar sin una instalación real (AD-1).
/// </para>
/// <para>
/// La fuente NO es un estado de la máquina del servidor: son las operaciones en curso
/// que el PROPIO cliente declara, con su tipo y su estado (AD-22).
/// </para>
/// </summary>
public interface IActiveInstallationSource
{
    /// <summary>
    /// Reads the installation operations that the client is running right now, ordered by
    /// operation identifier (AD-22).
    /// </summary>
    /// <returns>The installation operations that are in course.</returns>
    IReadOnlyList<ClientStateChanged.ActiveOperation> ListActiveOperations();
}
