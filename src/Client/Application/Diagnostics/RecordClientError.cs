using System.Security.Cryptography;
using System.Text;
using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Diagnostics;

/// <summary>
/// Use case that REGISTERS an error of the client, both in its own database and in the
/// server (AD-1, AD-11, AD-15, AD-18, AD-22, AD-23, NFR-7, AC1, AC2, AC3).
///
/// <para>
/// El caso de uso es el ÚNICO dueño del ORDEN NORMATIVO del diagnóstico del cliente:
/// (1) derivar el identificador del error; (2) PERSISTIRLO en la base de datos del
/// cliente; y (3) PUBLICARLO al servidor como <see cref="ClientErrorReported"/> (AC1,
/// AC2).
/// </para>
/// <para>
/// AD-1, AD-15: la PERSISTENCIA PRECEDE a la publicación y la publicación NUNCA impide
/// que el error quede registrado. Un fallo del hub se registra y se sigue: lo que
/// sobrevive al reinicio del equipo es la fila local, así que el diagnóstico no se pierde
/// por un servidor que no está (AD-11, NFR-7).
/// </para>
/// <para>
/// AD-18, AD-22: el cliente NO acuña el identificador del error del servidor. El
/// identificador que viaja en el payload es el identificador de la OPERACIÓN, y el
/// servidor deriva el suyo del reporte, así que la publicación es idempotente por
/// completo (AD-22).
/// </para>
/// <para>
/// AD-23: el error lleva el <c>clientId</c> y el <c>operationId</c> del cliente, que son
/// los MISMOS identificadores que viajan en el log del servidor, así que un error del
/// servidor y su correspondiente en el cliente se pueden UNIR (AC3, AR-20).
/// </para>
/// </summary>
public sealed class RecordClientError
{
    private readonly IClientStateStore _stateStore;
    private readonly IClientErrorPublisher? _publisher;
    private readonly ClientIdentityProvider _identity;
    private readonly ILogger<RecordClientError> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates the use case that registers the errors of the client.
    /// </summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="publisher">
    /// Port of the publication of the error al servidor, or <c>null</c> when the client
    /// does not publish it. Sin publicador el error queda PERSISTIDO y no se publica: la
    /// proyección es un complemento y no su motivo de vida (AD-1).
    /// </param>
    /// <param name="identity">Source of the canonical identifier of the team (AD-23).</param>
    /// <param name="logger">Log of the use case.</param>
    /// <param name="time">Source of the instant of the error.</param>
    public RecordClientError(
        IClientStateStore stateStore,
        IClientErrorPublisher? publisher,
        ClientIdentityProvider identity,
        ILogger<RecordClientError> logger,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(logger);

        _stateStore = stateStore;
        _publisher = publisher;
        _identity = identity;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Registers an error of the client, with its FOUR parts and its association to the
    /// operation that produced it (AC1, AC2).
    /// </summary>
    /// <param name="error">Error of the catalog that explains the failure.</param>
    /// <param name="operationId">
    /// Identifier of the unit of work that produced the error, or <c>null</c> when the
    /// error is not bound to one (AD-23).
    /// </param>
    /// <param name="operationType">
    /// Kind of operation that produced the error: a download, an installation or a launch
    /// (AC2).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the error has been registered.</returns>
    public async Task RecordAsync(
        ApiError error,
        string? operationId,
        string operationType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);

        var clientId = _identity.CanonicalId;
        var occurredAt = _time.GetUtcNow();

        // AD-1, AD-15: la PERSISTENCIA PRECEDE a la publicación. Un fallo del hub no
        // puede impedir que el error quede en la base de datos del cliente, así que la
        // escritura va PRIMERO y la publicación va detrás (AD-11, NFR-7).
        await _stateStore
            .SaveErrorAsync(
                new PersistedClientError(
                    DeriveErrorId(clientId, operationId, error.Code),
                    clientId,
                    operationId ?? string.Empty,
                    operationType,
                    error.Code,
                    error.Message,
                    error.Cause,
                    error.NextStep,
                    occurredAt),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogError(
            "El cliente registra el error {Code} de la operación {OperationId} de tipo {OperationType}: {Message} {Cause} {NextStep}",
            error.Code,
            operationId,
            operationType,
            error.Message,
            error.Cause,
            error.NextStep);

        if (_publisher is null)
        {
            return;
        }

        try
        {
            // AD-18, AD-22: el payload es el CONTRATO EXACTO `ClientErrorReported`, y el
            // servidor deriva el identificador del error del reporte, así que el cliente
            // NO acuña ninguno (AD-22).
            await _publisher
                .PublishErrorAsync(
                    new ClientErrorReported(
                        clientId,
                        operationId ?? string.Empty,
                        error),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // AD-1, AD-15: la publicación NUNCA impide que el error quede registrado. El
            // hub puede no estar, y el diagnóstico local ya está en el disco (AD-11).
            _logger.LogWarning(
                exception,
                "No se pudo publicar el error {Code} del cliente {ClientId}: el error queda registrado en la base de datos del cliente.",
                error.Code,
                clientId);
        }
    }

    /// <summary>
    /// Derives the identifier of the error from the team, the operation and the stable
    /// code, de forma DETERMINISTA (AD-22).
    ///
    /// <para>
    /// El mismo error registrado dos veces produce el MISMO identificador, así que la
    /// escritura local es idempotente y un reintento NO duplica el diagnóstico que el
    /// jugador ve (AD-22).
    /// </para>
    /// <para>
    /// El separador es un carácter que no aparece en el identificador del cliente ni en
    /// el de la operación, así que dos tripletas distintas nunca producen el mismo
    /// identificador.
    /// </para>
    /// </summary>
    /// <param name="clientId">Canonical identifier of the team.</param>
    /// <param name="operationId">Identifier of the operation, or <c>null</c>.</param>
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <returns>The identifier of the error, stable for the same report.</returns>
    public static string DeriveErrorId(string clientId, string? operationId, string code)
    {
        var material = string.Join(
            '\u001F',
            clientId ?? string.Empty,
            operationId ?? string.Empty,
            code ?? string.Empty);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
