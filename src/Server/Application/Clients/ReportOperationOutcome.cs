using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that records the TERMINAL result of an operation that a client reports
/// (AD-18, AD-22, AD-33).
///
/// <para>
/// El resultado es un RESULTADO y NUNCA un error: un resultado
/// <see cref="OperationOutcome.PackageWithdrawn"/> NO produce <see cref="ApiError"/>, NO
/// se persiste como <c>ClientError</c> y NO se registra en el
/// <see cref="ErrorCatalog"/> (AD-33, R-MVP-8).
/// </para>
/// <para>
/// El resultado se guarda en la columna <c>outcome</c> de <c>DOWNLOAD_INTENT</c>
/// cuando la operación tiene la INTENCIÓN registrada, y se publica en la vista de
/// organización con el texto de la ÚNICA tabla de microcopia (AD-25, AD-33).
/// </para>
/// </summary>
public sealed class ReportOperationOutcome
{
    private readonly IClientRegistrationRepository _repository;
    private readonly IDownloadIntentRepository _intents;
    private readonly ClientOperationsProjection _operations;
    private readonly ILogger<ReportOperationOutcome> _logger;

    /// <summary>Creates the use case that records the terminal result of an operation.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="intents">Port of the registry of the download intentions.</param>
    /// <param name="operations">Projection of the operations that are in course.</param>
    /// <param name="logger">Log of the result of the operation.</param>
    public ReportOperationOutcome(
        IClientRegistrationRepository repository,
        IDownloadIntentRepository intents,
        ClientOperationsProjection operations,
        ILogger<ReportOperationOutcome> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _intents = intents;
        _operations = operations;
        _logger = logger;
    }

    /// <summary>
    /// Records the terminal result that the client reports (AD-33).
    ///
    /// <para>
    /// La proyección es IDEMPOTENTE por <c>(clientId, operationId)</c>: el mismo
    /// resultado reportado dos veces NO crea un segundo resultado ni un segundo error
    /// (AD-22, AD-33).
    /// </para>
    /// </summary>
    /// <param name="reported">Result that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the result has been recorded.</returns>
    public async Task ExecuteAsync(
        OperationOutcomeReported reported,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reported);

        var existing = await _repository
            .FindByClientIdAsync(reported.ClientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que el
            // resultado de un cliente que no está registrado no crea ninguna fila.
            _logger.LogWarning(
                "El cliente {ClientId} publica el resultado de la operación {OperationId} pero no está registrado: la publicación se ignora.",
                reported.ClientId,
                reported.OperationId);

            return;
        }

        // AD-10, AD-25: un resultado que la tabla de microcopia NO reconoce no se
        // inventa y no se guarda: el resto de la proyección se conserva.
        if (!_operations.RecordOutcome(reported.ClientId, reported.OperationId, reported.Outcome))
        {
            _logger.LogWarning(
                "El cliente {ClientId} declara un resultado de operación desconocido para la operación {OperationId}: el resultado no se registra.",
                reported.ClientId,
                reported.OperationId);

            return;
        }

        // AD-33: el resultado se guarda en la columna `outcome` de `DOWNLOAD_INTENT`
        // cuando la operación tiene la intención registrada.
        await RecordIntentOutcomeAsync(reported, cancellationToken).ConfigureAwait(false);

        // AD-22, AD-33: la operación TERMINADA deja de estar en curso. La operación es
        // del cliente que la declaró, así que el resultado de un cliente NUNCA cierra la
        // operación de otro (AD-22).
        if (_operations.RemoveOperation(reported.ClientId, reported.OperationId))
        {
            _logger.LogInformation(
                "El cliente {ClientId} cierra la operación {OperationId} con el resultado {Outcome}.",
                reported.ClientId,
                reported.OperationId,
                reported.Outcome);
        }

        _logger.LogInformation(
            "El cliente {ClientId} declara el resultado {Outcome} de la operación {OperationId}.",
            reported.ClientId,
            reported.Outcome,
            reported.OperationId);
    }

    /// <summary>
    /// Writes the result in the column <c>outcome</c> of the intention of the download,
    /// when that intention exists AND belongs to the client that reports the result
    /// (AD-33).
    ///
    /// <para>
    /// AD-22: la intención de una descarga tiene un dueño —el cliente que la registró—,
    /// así que un cliente NO puede escribir el resultado de la intención de OTRO. La
    /// comparación es EXACTA del identificador del cliente, así que una publicación de
    /// otro cliente no altera la fila que no es suya (AD-22).
    /// </para>
    /// </summary>
    /// <param name="reported">Result that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention has been updated.</returns>
    private async Task RecordIntentOutcomeAsync(
        OperationOutcomeReported reported,
        CancellationToken cancellationToken)
    {
        var intent = await _intents
            .FindAsync(reported.OperationId, cancellationToken)
            .ConfigureAwait(false);

        if (intent is null)
        {
            // La operación NO tiene intención registrada —una instalación, por ejemplo—:
            // no hay fila que actualizar y el resultado vive solo en la proyección.
            return;
        }

        if (!string.Equals(intent.ClientId, reported.ClientId, StringComparison.Ordinal))
        {
            // AD-22: la intención es de OTRO cliente. El resultado se conserva en la
            // proyección del cliente que lo declaró, pero la fila que no es suya NO se
            // toca (AD-33).
            _logger.LogWarning(
                "El cliente {ClientId} declara un resultado para la operación {OperationId}, cuya intención pertenece a otro cliente: la intención no se modifica.",
                reported.ClientId,
                reported.OperationId);

            return;
        }

        await _intents
            .SaveOutcomeAsync(
                new DownloadIntentRow(
                    intent.OperationId,
                    intent.ClientId,
                    intent.PackageId,
                    intent.State,
                    reported.Outcome.ToString()),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
