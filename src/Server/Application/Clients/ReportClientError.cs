using System.Security.Cryptography;
using System.Text;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that REGISTERS the error that a client reports (AD-15, AD-18, AD-22, FR-10).
///
/// <para>
/// El error que viaja en el payload es SIEMPRE de la única forma de error del sistema,
/// <see cref="ApiError"/>, con su código estable del catálogo, su mensaje, su CAUSA y su
/// SIGUIENTE PASO (AD-15, AR-4).
/// </para>
/// <para>
/// El caso de uso PERSISTE el error en el registro del servidor, así que el organizador
/// lo ve en la fila del equipo sin pedir nada aparte, y con las tres partes que le
/// dicen si reintenta o si necesita asistencia (AD-15, AD-22, UX-DR-44).
/// </para>
/// <para>
/// El cliente que publica es SIEMPRE el de la fila: una publicación de un cliente que
/// NO está registrado se ignora, porque el alta es la operación de registro (AD-30,
/// AD-32).
/// </para>
/// </summary>
public sealed class ReportClientError
{
    private readonly IClientRegistrationRepository _repository;
    private readonly IClientErrorRepository _errors;
    private readonly ILogger<ReportClientError> _logger;

    /// <summary>Creates the use case that registers the error of a client.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="errors">Port of the registry of the errors of the clients.</param>
    /// <param name="logger">Log of the error of the client.</param>
    public ReportClientError(
        IClientRegistrationRepository repository,
        IClientErrorRepository errors,
        ILogger<ReportClientError> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _errors = errors;
        _logger = logger;
    }

    /// <summary>
    /// Registers the error that the client reports, con TODAS sus partes (AD-15).
    ///
    /// <para>
    /// El error se guarda con la forma EXACTA de la tabla <c>CLIENT_ERROR</c>: el
    /// identificador del error, el del cliente, el de la operación, el código estable,
    /// el mensaje, la causa, el siguiente paso y el instante (AD-15, AD-22).
    /// </para>
    /// <para>
    /// Un error SIN código, SIN mensaje, SIN causa o SIN siguiente paso NO se registra:
    /// un error sin sus tres partes no le dice nada al organizador, así que se registra
    /// el hueco y no se escribe una fila incompleta (AD-15, UX-DR-36).
    /// </para>
    /// </summary>
    /// <param name="reported">Error that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the error has been registered.</returns>
    public async Task ExecuteAsync(
        ClientErrorReported reported,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reported);

        var existing = await _repository
            .FindByClientIdAsync(reported.ClientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que el
            // error de un cliente que no está registrado no crea ninguna fila.
            _logger.LogWarning(
                "El cliente {ClientId} reporta un error pero no está registrado: el error se ignora.",
                reported.ClientId);

            return;
        }

        if (!IsComplete(reported.Error))
        {
            _logger.LogWarning(
                "El cliente {ClientId} reporta un error sin sus tres partes: el error no se registra.",
                reported.ClientId);

            return;
        }

        // AD-15: el error se registra con su identificador, así que la vista de
        // organización lo muestra con su código, su mensaje, su causa y su siguiente
        // paso (AD-22, UX-DR-44).
        //
        // AD-22: el identificador del error es ESTABLE y se DERIVA del reporte, así que
        // la republicación del mismo error —una reconexión, un reintento de publicación—
        // NO duplica el diagnóstico que el organizador ve (AD-15, AD-22). Un identificador
        // volátil rompería esa idempotencia y dejaría dos filas del mismo error.
        var registered = new ClientErrorWrite(
            DeriveErrorId(reported),
            reported.ClientId,
            reported.OperationId ?? string.Empty,
            reported.Error.Code,
            reported.Error.Message,
            reported.Error.Cause,
            reported.Error.NextStep,
            DateTimeOffset.UtcNow);

        await _errors.SaveAsync(registered, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "El cliente {ClientId} registra el error {ErrorCode} de la operación {OperationId}.",
            reported.ClientId,
            reported.Error.Code,
            registered.OperationId);
    }

    /// <summary>
    /// Tells whether the error carries the THREE parts that lo hacen útil: el código
    /// estable, el mensaje, la causa y el siguiente paso (AD-15, UX-DR-36).
    /// </summary>
    /// <param name="error">Error that the client declared.</param>
    /// <returns><c>true</c> when the error carries all its parts.</returns>
    private static bool IsComplete(ApiError? error) =>
        error is not null
        && !string.IsNullOrWhiteSpace(error.Code)
        && !string.IsNullOrWhiteSpace(error.Message)
        && !string.IsNullOrWhiteSpace(error.Cause)
        && !string.IsNullOrWhiteSpace(error.NextStep);

    /// <summary>
    /// Derives the identifier of the error from the report, de forma DETERMINISTA (AD-22).
    ///
    /// <para>
    /// El identificador es el hash del equipo, de la operación y del código estable del
    /// error: el MISMO error reportado dos veces produce el MISMO identificador, así que
    /// la escritura es idempotente por `errorId` y una republicación del cliente NO
    /// duplica el diagnóstico que el organizador ve (AD-15, AD-22).
    /// </para>
    /// <para>
    /// El contrato `ClientErrorReported` NO lleva identificador de error: su forma
    /// EXACTA es `clientId`, `operationId` y `error` (AD-18, AD-22), así que el
    /// identificador lo deriva el servidor a partir de lo que el cliente declara.
    /// </para>
    /// <para>
    /// El separador es un carácter que no aparece en el identificador del cliente ni en
    /// el de la operación, así que dos tripletas distintas nunca producen el mismo
    /// identificador.
    /// </para>
    /// </summary>
    /// <param name="reported">Error that the client declared.</param>
    /// <returns>The identifier of the error, stable for the same report.</returns>
    private static string DeriveErrorId(ClientErrorReported reported)
    {
        var material = string.Join(
            '\u001F',
            reported.ClientId ?? string.Empty,
            reported.OperationId ?? string.Empty,
            reported.Error.Code ?? string.Empty);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
