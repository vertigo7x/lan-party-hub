using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// Use case of the connection with the event server by the address that the player typed
/// (AC1, AC3, FR-3, AD-3, AD-27).
///
/// <para>
/// The use case parses the address with the SINGLE rule of the Application layer, opens
/// the connection with a time limit and returns the attempt: the endpoint, the state of
/// the connection and, when the attempt failed, the error of the catalog con sus tres
/// partes (AC1, AC3, AR-4).
/// </para>
/// <para>
/// AD-6, FR-4: después de abrir la conexión, el caso de uso SONDEA la versión del
/// servidor. El servidor es el que decide la compatibilidad: si el cliente no es
/// compatible, el intento termina DESCONECTADO con el diagnóstico de la versión, y el
/// cliente NO sincroniza, NO descarga y NO instala (AD-6).
/// </para>
/// <para>
/// Un fallo NEVER viaja al caller como una excepción: una dirección que no responde
/// ends the attempt as <see cref="ConnectionState.Disconnected"/> con el mensaje de tres
/// partes, y el jugador puede volver a intentarlo (AC3, UX-DR-36).
/// </para>
/// <para>
/// El caso de uso depende del puerto declarado en esta capa y nunca de un adaptador,
/// así que puede ejercitarse sin una red (AD-1).
/// </para>
/// </summary>
public sealed class ServerConnectionService
{
    /// <summary>
    /// Time that the opening of the connection lasts at most, when the caller does not fix
    /// another one (AC3).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly IServerConnector _connector;
    private readonly IServerVersionProbe _versionProbe;
    private readonly IClientVersionProvider _versions;
    private readonly ITrustAnchorReader? _anchorReader;
    private readonly IServerTrustAnchorProbe? _anchorProbe;
    private readonly ILogger<ServerConnectionService> _logger;

    /// <summary>Creates the use case of the connection with the event server.</summary>
    /// <param name="connector">Port of the opening of the connection with the server.</param>
    /// <param name="versionProbe">Port of the probe of the versions of the server.</param>
    /// <param name="versions">Versions that the client declares in every request.</param>
    /// <param name="anchorReader">
    /// Port of the read of the ancla de confianza que ya está fijada, or <c>null</c> when
    /// the client does not have a database of state (AD-27).
    /// </param>
    /// <param name="anchorProbe">
    /// Port of the probe of la clave que el servidor presenta, or <c>null</c> when the
    /// client cannot validate the ancla fijada (AD-27).
    /// </param>
    /// <param name="logger">Log of the use case.</param>
    public ServerConnectionService(
        IServerConnector connector,
        IServerVersionProbe versionProbe,
        IClientVersionProvider versions,
        ITrustAnchorReader? anchorReader,
        IServerTrustAnchorProbe? anchorProbe,
        ILogger<ServerConnectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(versionProbe);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(logger);

        _connector = connector;
        _versionProbe = versionProbe;
        _versions = versions;
        _anchorReader = anchorReader;
        _anchorProbe = anchorProbe;
        _logger = logger;
    }

    /// <summary>
    /// Connects with the server of the event at the address that the player typed, with
    /// the default time limit (AC1, AC3).
    /// </summary>
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The attempt of the connection.</returns>
    public Task<ConnectionAttempt> ConnectAsync(
        string? address,
        CancellationToken cancellationToken) =>
        ConnectAsync(address, DefaultTimeout, cancellationToken);

    /// <summary>
    /// Connects with the server of the event at the address that the player typed, with the
    /// given time limit (AC1, AC3).
    ///
    /// <para>
    /// A malformed address NEVER reaches the network: the attempt ends as disconnected
    /// with the three part message of the catalog (AC3, AR-4).
    /// </para>
    /// </summary>
    /// <param name="address">Text of the address, in the form <c>host:puerto</c>.</param>
    /// <param name="timeout">Time that the opening lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The attempt of the connection.</returns>
    public async Task<ConnectionAttempt> ConnectAsync(
        string? address,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        if (!ServerEndpointParser.TryParse(address, out var endpoint, out var parseError))
        {
            _logger.LogWarning(
                "La dirección escrita por el jugador no es válida: el intento termina como desconectado con el código {ErrorCode}.",
                parseError!.Code);

            return ConnectionAttempt.Failed(null, parseError);
        }

        try
        {
            await _connector
                .ConnectAsync(endpoint!, timeout, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "El cliente se conectó al servidor {ServerAddress}:{ServerPort}; el ancla de confianza queda pendiente.",
                endpoint!.Address,
                endpoint.Port);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La cancelación la pidió el jugador: no es un fallo de la red y el token
            // viaja intacto (AC3).
            throw;
        }
        catch (Exception exception)
        {
            // El destino puede no responder o puede rechazar la conexión: el cliente NO
            // falla de forma fatal y la vía manual sigue operativa (AC1, AC3, AD-3).
            _logger.LogWarning(
                exception,
                "No se pudo abrir la conexión con {ServerAddress}:{ServerPort}: el intento termina como desconectado.",
                endpoint!.Address,
                endpoint.Port);

            return ConnectionAttempt.Failed(
                endpoint,
                ErrorCatalog.Resolve(ErrorCatalog.UnknownErrorCode));
        }

        // AD-6, FR-4: la conexión se abrió, así que ahora hay que comprobar que el
        // servidor ACEPTA la versión del cliente. El servidor decide, y el cliente solo
        // interpreta su respuesta.
        var probe = await _versionProbe
            .ProbeAsync(endpoint!, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (!probe.Compatible)
        {
            _logger.LogWarning(
                "El cliente {ClientVersion} no es compatible con el servidor {ServerVersion}: el intento termina como desconectado.",
                _versions.ClientVersion,
                probe.ServerVersion ?? "(desconocida)");

            return ConnectionAttempt.Incompatible(
                endpoint,
                ErrorCatalog.Resolve(ErrorCatalog.ClientVersionIncompatibleCode),
                _versions.ClientVersion,
                probe.ServerVersion);
        }

        // AD-27: la conexión se valida contra el ancla de confianza que YA está fijada. Un
        // ancla fijada es INMUTABLE, así que una clave distinta se RECHAZA y exige un
        // emparejamiento fuera de banda nuevo: no es un aviso ni un reintento automático
        // (AC6, AD-27).
        var anchorValidation = await ValidateFixedAnchorAsync(endpoint!, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (anchorValidation is not null)
        {
            return anchorValidation;
        }

        return ConnectionAttempt.Succeeded(endpoint);
    }

    /// <summary>
    /// Validates the server against the ancla de confianza that the client already fixed
    /// (AC6, AD-27).
    ///
    /// <para>
    /// AD-27: la validación compara la clave que el servidor presenta con la clave que ya
    /// está PERSISTIDA, y no con un fingerprint recién recibido. Un ancla fijada no se
    /// sustituye: una clave distinta se rechaza y exige un emparejamiento nuevo, explícito
    /// y fuera de banda (AC6, AD-27).
    /// </para>
    /// <para>
    /// Cuando el cliente NO tiene ancla fijada, la validación no dice nada: la conexión
    /// queda en <see cref="ConnectionState.AnchorUnverified"/>, que es un estado de
    /// seguridad y NO un error de red (AC7, AD-27).
    /// </para>
    /// </summary>
    /// <param name="endpoint">Endpoint of the server of the event.</param>
    /// <param name="timeout">Time that the validation lasts at most.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The attempt of the connection when the ancla NO coincide, or <c>null</c> when the
    /// server presents the ancla that is already fixed or when the client has not fixed
    /// any.
    /// </returns>
    private async Task<ConnectionAttempt?> ValidateFixedAnchorAsync(
        ServerEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_anchorReader is null || _anchorProbe is null)
        {
            return null;
        }

        var fixedAnchor = await _anchorReader
            .LoadTrustAnchorAsync(cancellationToken)
            .ConfigureAwait(false);

        if (fixedAnchor is null)
        {
            // Sin ancla fijada la conexión queda «Ancla sin verificar»: el estado de
            // seguridad que impide descargar e instalar (AC7, AD-27).
            return null;
        }

        var probe = await _anchorProbe
            .ProbeAsync(endpoint, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (probe.Anchor is null)
        {
            // El servidor no publicó ninguna ancla: el cliente NO puede confirmar que la
            // clave es la fijada, así que la conexión no se da por verificada. El rechazo
            // no se afirma, porque el servidor puede que no haya publicado nada por un
            // motivo que no sea un ataque (AC3, AD-27).
            _logger.LogWarning(
                "El servidor {ServerAddress}:{ServerPort} no publicó el ancla de confianza que el cliente tiene fijada: la conexión queda sin ancla verificada.",
                endpoint.Address,
                endpoint.Port);

            return null;
        }

        AnchorFingerprint presented;

        try
        {
            presented = AnchorFingerprint.FromPublicKey(probe.Anchor.PublicKey);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(
                exception,
                "El servidor {ServerAddress}:{ServerPort} publicó una clave de ancla que no es válida: la conexión queda sin ancla verificada.",
                endpoint.Address,
                endpoint.Port);

            return null;
        }

        if (fixedAnchor.AnchorFingerprint.Matches(presented))
        {
            return null;
        }

        // AD-27: la clave que presenta el servidor NO es la que está fijada. Se RECHAZA, no
        // se sustituye, y el cliente exige un emparejamiento fuera de banda nuevo. El
        // `code` del error es el de «fingerprint de ancla no coincide», DISTINTO del de
        // «firma inválida» (AC2, AC6, AD-27).
        _logger.LogWarning(
            "El servidor {ServerAddress}:{ServerPort} presenta un ancla ({PresentedFingerprint}) que NO es la que el cliente tiene fijada ({FixedFingerprint}): el cliente la rechaza y exige un emparejamiento fuera de banda nuevo.",
            endpoint.Address,
            endpoint.Port,
            presented.Value,
            fixedAnchor.Fingerprint);

        return ConnectionAttempt.AnchorMismatch(
            endpoint,
            ErrorCatalog.Resolve(ErrorCatalog.AnchorFingerprintMismatchCode),
            presented.Value,
            fixedAnchor.Fingerprint);
    }
}
