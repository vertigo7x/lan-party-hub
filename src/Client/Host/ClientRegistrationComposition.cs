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
    public ClientRegistrationComposition(
        Uri serverAddress,
        IClientRequestHeaders headers,
        ClientIdentity identity,
        IClientEffectiveNameStore nameStore,
        IClientStatePublisher statePublisher,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nameStore);
        ArgumentNullException.ThrowIfNull(statePublisher);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Identity = identity;

        _statePublisher = statePublisher;

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
    /// Publishes the state of the connection of the client, so the server persists it
    /// and the table of clients reflects it (AD-18, AD-29, FR-10).
    /// </summary>
    /// <param name="connectionState">Connection state that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    public async Task PublishStateAsync(
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        try
        {
            await _statePublisher
                .PublishAsync(
                    new ClientStateChanged(
                        Identity.CanonicalId,
                        connectionState,
                        [],
                        []),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación del estado NO impide que el jugador siga usando el
            // cliente: el estado es un complemento y no su motivo de vida (AD-1).
            Logger.LogWarning(
                exception,
                "No se pudo publicar el estado de la conexión del equipo: el cliente sigue operando.");
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
