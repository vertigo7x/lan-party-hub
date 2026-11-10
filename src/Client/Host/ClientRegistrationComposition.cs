using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Connection;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the registration of the client against the event server
/// (AD-30, AD-32).
///
/// <para>
/// The composition opens the adapter that speaks HTTP with the server and the use case
/// that registers the team. It is the ONLY place that knows the adapters: the use case
/// depends on the ports declared in the Application layer and never on their
/// implementations (AD-1).
/// </para>
/// <para>
/// AD-30, AD-32: la composición REGISTRA al equipo cada vez que el servidor pasa a
/// estar disponible, y la misma operación cubre la reconexión y el renombrado: el
/// cliente envía su identificador y el nombre efectivo que ya tiene, y el servidor lo
/// conserva. Un cliente reconectado nunca colisiona consigo mismo.
/// </para>
/// <para>
/// Un fallo al registrar NO impide que el cliente arranque: el registro es un
/// complemento del cliente y no su motivo de vida, así que el cliente conserva su
/// catálogo y sus instalaciones (AD-1).
/// </para>
/// </summary>
public sealed class ClientRegistrationComposition : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly RegisterWithServer _registerWithServer;
    private readonly IClientStatePublisher _statePublisher;
    private readonly IClientConnectionPublisher? _connectionPublisher;
    private readonly ClientStateHubPublisher? _ownedPublisher;
    private readonly IClientStateStore? _stateStore;
    private readonly IActiveInstallationSource? _activeInstallations;
    private readonly Lock _connectionStateGate = new();

    private ConnectionState _lastConnectionState = ConnectionState.Disconnected;

    /// <summary>Creates the composition of the registration against the event server.</summary>
    /// <param name="serverAddress">Address of the event server.</param>
    /// <param name="headers">
    /// Source of the identifiers of correlation that the requests carry. It carries the
    /// canonical identifier of the team, which the server REQUIRES in every request of
    /// the surface of the client (AD-15, AD-23).
    /// </param>
    /// <param name="identity">Identity of the team.</param>
    /// <param name="nameStore">Access to the effective name of the team.</param>
    /// <param name="statePublisher">Publisher of the state of the connection of the client.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="stateStore">
    /// Access to the local database of the client, de donde salen las instalaciones y las
    /// operaciones REALES que el cliente publica (AD-22). When it is <c>null</c> the
    /// composition does NOT publish the state: sin store local el cliente no tiene una
    /// instantánea completa que pueda sostener, y publicarla vacía vaciaría por error la
    /// proyección del servidor (AD-22).
    /// </param>
    /// <param name="activeInstallations">
    /// Source of the INSTALLATION operations that the client is running right now, or
    /// <c>null</c> when the client does not track them. Sin fuente, la composición
    /// publica el estado sin las instalaciones en curso, y la publicación del resto del
    /// estado no se rompe (AD-1, AD-22).
    /// </param>
    /// <param name="connectionPublisher">
    /// Port of the publication of the CONNECTION state ALONE, or <c>null</c> when the
    /// client cannot publish it. Sin publicador la conexión NO se comunica por separado:
    /// el servidor la conoce por la vía del REGISTRO (AD-29, AD-32).
    /// </param>
    public ClientRegistrationComposition(
        Uri serverAddress,
        IClientRequestHeaders headers,
        ClientIdentity identity,
        IClientEffectiveNameStore nameStore,
        IClientStatePublisher statePublisher,
        ILoggerFactory loggerFactory,
        IClientStateStore? stateStore = null,
        IActiveInstallationSource? activeInstallations = null,
        IClientConnectionPublisher? connectionPublisher = null,
        ClientStateHubPublisher? ownedPublisher = null)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nameStore);
        ArgumentNullException.ThrowIfNull(statePublisher);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Identity = identity;

        _statePublisher = statePublisher;
        _connectionPublisher = connectionPublisher;
        _ownedPublisher = ownedPublisher;
        _stateStore = stateStore;
        _activeInstallations = activeInstallations;
        // AD-6: la versión del cliente es la del binario que se está ejecutando y la
        // versión del protocolo es la constante del contrato compartido.
        var versions = new ClientBuildVersion();

        // AD-6, AD-15, AD-23: el manejador de cabeceras estampa la versión del cliente,
        // la del protocolo y el identificador del equipo en TODA petición, así que el
        // servidor puede pronunciarse sobre la compatibilidad (AD-6, AD-15).
        //
        // El manejador es un DelegatingHandler y necesita un manejador interior que sea
        // el que abre la conexión: sin él, TODA petición del cliente falla antes de
        // salir y el registro nunca llega al servidor (AD-15, AD-23).
        _httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, versions)
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = serverAddress,
        };

        _registerWithServer = new RegisterWithServer(
            new ClientRegistrationHttpClient(_httpClient),
            nameStore,
            versions,
            loggerFactory.CreateLogger<RegisterWithServer>());

        Logger = loggerFactory.CreateLogger("LANPartyHub.Client.Registration");
    }

    /// <summary>Identity of the team that this composition registers.</summary>
    public ClientIdentity Identity { get; }

    /// <summary>Log of the registration of the client.</summary>
    public ILogger Logger { get; }

    /// <summary>
    /// The LAST connection state that the client DECLARED, así que una publicación que
    /// el cliente no puede acompañar con un estado nuevo declara el que ya conoce
    /// (AD-29, AD-22).
    ///
    /// <para>
    /// El estado NUNCA se mejora por convenience: un cliente que no ha declarado nada
    /// declara <see cref="ConnectionState.Disconnected"/>, así que una publicación hecha
    /// sin conexión NO afirma que el equipo esté conectado (AD-29).
    /// </para>
    /// </summary>
    public ConnectionState LastConnectionState
    {
        get
        {
            lock (_connectionStateGate)
            {
                return _lastConnectionState;
            }
        }
    }

    /// <summary>
    /// Registers the team against the event server, proposing the EFFECTIVE name that
    /// the client already holds when it has one, so a reconnected client never
    /// collides with itself (AD-30, AD-32).
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the registration, with the rejection when there was one.</returns>
    public async Task<ClientRegistrationResult> RegisterAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        try
        {
            var effective = await _registerWithServer
                .ExecuteAsync(
                    Identity,
                    await ResolveProposedNameAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);

            // AD-29, FR-10: el estado de la conexión se PUBLICA aparte, en el canal
            // sancionado para el estado del cliente, y no en el cuerpo del registro,
            // cuya forma es EXACTA (AD-32).
            await PublishStateAsync(connectionState, cancellationToken).ConfigureAwait(false);

            return new ClientRegistrationResult(effective, null);
        }
        catch (ClientRegistrationRejectedException exception)
        {
            // AD-30, AD-32: el rechazo deja al cliente SIN REGISTRAR y sin poder
            // operar, así que el error con sus tres partes se COMUNICA a la superficie y
            // no se pierde en el log.
            Logger.LogWarning(
                "El servidor rechazó el registro del equipo con el código {ErrorCode}: el cliente queda sin registrar.",
                exception.Error.Code);

            return new ClientRegistrationResult(null, exception.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // El cliente no puede registrarse, pero sigue siendo un cliente que arranca:
            // el catálogo y las instalaciones no dependen del registro (AD-1).
            Logger.LogWarning(
                exception,
                "No se pudo registrar el equipo ante el servidor: el cliente sigue operando sin estar registrado.");

            return new ClientRegistrationResult(null, null);
        }
    }

    /// <summary>
    /// Publishes the state of the connection of the client, TOGETHER WITH the
    /// installations and the operations that it really has, so the server can project
    /// them and the table of clients reflects it (AD-18, AD-22, AD-29, FR-10).
    ///
    /// <para>
    /// La publicación se hace DESPUÉS del registro, y también en cada cambio y en cada
    /// reconexión: el cliente publica su estado real, no una lista vacía (AD-22).
    /// </para>
    /// <para>
    /// La publicación es una INSTANTÁNEA COMPLETA: el servidor sustituye la proyección
    /// que tenía por la que recibe. Cuando el cliente NO puede leer sus datos locales la
    /// publicación COMPLETA se OMITE y se REGISTRA, porque una instantánea vacía vaciaría
    /// por error la vista del organizador (AD-22).
    /// </para>
    /// <para>
    /// La comunicación de la CONEXIÓN se hace entonces por el evento de SOLO conexión:
    /// el cliente declara su estado de conexión sin juegos ni operaciones, así que el
    /// servidor refleja la desconexión SIN tocar la proyección que ya tenía de ese
    /// cliente (AD-18, AD-22, AD-29).
    /// </para>
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task PublishStateAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        // AD-29: el estado DECLARADO es el último que el cliente conoce, así que la
        // publicación posterior declara ese y no uno inventado.
        TrackConnectionState(connectionState);

        // AD-22: la publicación del estado es una INSTANTÁNEA COMPLETA: la lista de
        // operaciones que viaja es la lista COMPLETA de las del cliente, así que el
        // servidor sustituye la proyección que tenía por la que recibe. Publicar una
        // lista VACÍA cuando el cliente NO puede leer sus datos locales BORRARÍA la
        // proyección que el servidor ya tenía (AD-22).
        //
        // Por eso, sin store local NO se publica una instantánea: el cliente no tiene
        // una que pueda sostener, y la proyección del servidor se conserva. Lo que sí se
        // comunica es la CONEXIÓN, por el evento de SOLO conexión (AD-18, AD-22, AD-29).
        if (_stateStore is null)
        {
            Logger.LogWarning(
                "No se publica el estado del equipo: el cliente no tiene acceso a sus datos locales, así que no puede declarar una instantánea completa sin vaciar la proyección del servidor.");

            await PublishConnectionOnlyAsync(connectionState, cancellationToken).ConfigureAwait(false);

            return;
        }

        try
        {
            // AD-22: las instalaciones y las operaciones son LOCALES, y el cliente
            // publica su estado REAL. La ruta de la instalación no sale del cliente.
            //
            // AD-18, AD-22: las INSTALACIONES en curso son operaciones en curso como
            // las demás, así que la fuente de las instalaciones activas se entrega al
            // caso de uso y la tabla del organizador muestra la fase REAL.
            var publisher = new PublishClientState(
                _stateStore,
                _statePublisher,
                new FixedClientIdentity(Identity),
                _activeInstallations);

            await publisher
                .ExecuteAsync(connectionState, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // AD-22: una lectura local que falla NO se convierte en una instantánea
            // vacía: la proyección que el servidor ya tiene se CONSERVA, y la vista no se
            // vacía por error (AD-22).
            //
            // La publicación del estado NO impide que el jugador siga usando el cliente:
            // el estado es un complemento y no su motivo de vida (AD-1).
            Logger.LogWarning(
                exception,
                "No se pudo publicar el estado del equipo: el cliente sigue operando y la proyección del servidor se conserva.");

            // AD-18, AD-22, AD-29: la lectura local falló, así que el cliente declara
            // SOLO su conexión. El servidor refleja el estado de conexión sin tocar la
            // proyección que ya tenía de este equipo.
            await PublishConnectionOnlyAsync(connectionState, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes the CONNECTION state of the client ALONE, sin juegos ni operaciones
    /// (AD-18, AD-22, AD-29).
    ///
    /// <para>
    /// El evento existe para el cliente que no puede sostener una INSTANTÁNEA COMPLETA
    /// de su estado: el servidor refleja la conexión o la desconexión y CONSERVA la
    /// proyección que ya tenía de ese equipo (AD-22, AD-29).
    /// </para>
    /// <para>
    /// La publicación NUNCA impide que el cliente siga operando: es un complemento y no
    /// su motivo de vida (AD-1).
    /// </para>
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    private async Task PublishConnectionOnlyAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        if (_connectionPublisher is null)
        {
            return;
        }

        try
        {
            await _connectionPublisher
                .PublishAsync(
                    new ClientConnectionChanged(Identity.CanonicalId, connectionState),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación de la conexión NUNCA impide que el cliente siga operando
            // (AD-1, AD-22).
            Logger.LogWarning(
                exception,
                "No se pudo publicar la conexión del equipo: el cliente sigue operando.");
        }
    }

    /// <summary>
    /// Publishes the state of the client with the LAST connection state that the client
    /// DECLARED, así que una publicación que el cliente no puede acompañar con un estado
    /// nuevo no inventa ninguno (AD-29, AD-22).
    ///
    /// <para>
    /// Esta es la publicación que el tracker de las instalaciones ENLAZA TARDE: una
    /// operación que empieza o termina cambia la lista de operaciones en curso, y esa
    /// lista solo la publica el REGISTRO (AD-22, FR-10).
    /// </para>
    /// <para>
    /// Un cliente que no ha declarado ningún estado declara
    /// <see cref="ConnectionState.Disconnected"/>: la publicación NUNCA afirma una
    /// conexión que el cliente no tiene (AD-29).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public Task PublishCurrentStateAsync(CancellationToken cancellationToken) =>
        PublishStateAsync(LastConnectionState, cancellationToken);

    /// <summary>
    /// Remembers the connection state that the client DECLARED (AD-29).
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    private void TrackConnectionState(ConnectionState connectionState)
    {
        lock (_connectionStateGate)
        {
            _lastConnectionState = connectionState;
        }
    }

    /// <summary>
    /// Resolves the name that the client proposes: el nombre EFECTIVO que el servidor
    /// le devolvió la última vez, o el nombre de la máquina cuando todavía no se ha
    /// registrado nunca (AD-30, AD-32).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The name that the client proposes.</returns>
    private async Task<string> ResolveProposedNameAsync(CancellationToken cancellationToken)
    {
        var effective = await _registerWithServer
            .LoadEffectiveNameAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(effective))
        {
            return effective;
        }

        // AD-14, AD-30: la etiqueta de la máquina es el nombre humano del equipo. No es
        // una credencial: no habilita nada.
        //
        // El nombre que se propone tiene que ser ADMISIBLE por las reglas de forma del
        // servidor, así que el cliente lo DEPURA antes de proponerlo: un nombre de
        // máquina con un carácter fuera del conjunto o más largo de lo admitido sería
        // rechazado y dejaría al equipo sin registrar (AD-30, AD-32).
        return Sanitize(Environment.MachineName);
    }

    /// <summary>
    /// Makes a name ADMISSIBLE by the rules of shape of the server: only letters,
    /// digits, <c>.</c>, <c>-</c> and <c>_</c>, at most 64 code points, and never
    /// empty (AD-30).
    ///
    /// <para>
    /// La depuración es del CLIENTE y no del servidor: el servidor valida y rechaza, y
    /// el cliente propone un nombre que ya cumple la forma (AD-30, AD-32).
    /// </para>
    /// </summary>
    /// <param name="value">Name to sanitize.</param>
    /// <returns>The admissible name.</returns>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "equipo";
        }

        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            {
                builder.Append(character);
            }
        }

        var sanitized = builder.ToString();

        if (sanitized.Length == 0)
        {
            return "equipo";
        }

        // La longitud se acota en PUNTOS DE CÓDIGO, igual que la del servidor, así que
        // un nombre con emoji o con ideogramas no se recorta por la mitad (AD-30).
        var trimmed = new System.Text.StringBuilder(sanitized.Length);

        foreach (var rune in sanitized.EnumerateRunes().Take(64))
        {
            trimmed.Append(rune.ToString());
        }

        return trimmed.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
        _ownedPublisher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Outcome of the registration of the team against the event server (AD-30, AD-32).
///
/// <para>
/// La <c>Rejection</c> es el error del catálogo que explica el rechazo del servidor,
/// con sus tres partes, y es <c>null</c> cuando el registro NO fue rechazado. La
/// superficie la presenta TAL CUAL, sin inventar texto fuera de la tabla de microcopia
/// (AD-25, AD-30).
/// </para>
/// </summary>
/// <param name="Outcome">Outcome of the registration, or <c>null</c> when it could not happen.</param>
/// <param name="Rejection">Error of the catalog que explica el rechazo, o <c>null</c>.</param>
public sealed record ClientRegistrationResult(
    ClientRegistrationOutcome? Outcome,
    ApiError? Rejection);

/// <summary>
/// Source of the canonical identifier of the client, sobre la identidad que la
/// composición ya tiene (AD-23).
/// </summary>
/// <param name="identity">Identity of the team.</param>
internal sealed class FixedClientIdentity : LANPartyHub.Client.Application.Clients.ClientIdentityProvider
{
    /// <summary>Creates the source over an identity.</summary>
    /// <param name="identity">Identity of the team.</param>
    public FixedClientIdentity(ClientIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Identity = identity;
    }

    /// <summary>Identity of the team.</summary>
    public ClientIdentity Identity { get; }

    /// <inheritdoc />
    public string CanonicalId => Identity.CanonicalId;
}
