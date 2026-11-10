using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Clients;

/// <summary>
/// Use case that projects the PROGRESS that a client reports about its operations
/// (AD-18, AD-22, FR-10).
///
/// <para>
/// El progreso de una descarga es el de los BYTES REALES que el cliente transfirió, y
/// el de una instalación es el de su FASE REAL: el servidor NUNCA inventa un porcentaje
/// que el cliente no declaró (AD-22).
/// </para>
/// <para>
/// El cliente que publica es SIEMPRE el de la fila: una publicación de un cliente que
/// NO está registrado se ignora, porque el alta es la operación de registro (AD-30,
/// AD-32).
/// </para>
/// </summary>
public sealed class ReportClientProgress
{
    private readonly IClientRegistrationRepository _repository;
    private readonly ClientOperationsProjection _operations;
    private readonly ILogger<ReportClientProgress> _logger;

    /// <summary>Creates the use case that projects the progress of the client.</summary>
    /// <param name="repository">Port of the registry of the clients.</param>
    /// <param name="operations">Projection of the operations that are in course.</param>
    /// <param name="logger">Log of the progress of the client.</param>
    public ReportClientProgress(
        IClientRegistrationRepository repository,
        ClientOperationsProjection operations,
        ILogger<ReportClientProgress> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _operations = operations;
        _logger = logger;
    }

    /// <summary>
    /// Projects the progress of a download operation (AD-22, FR-10).
    /// </summary>
    /// <param name="progress">Progress that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the progress has been projected.</returns>
    public async Task ExecuteAsync(
        DownloadProgressUpdated progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!await KnowsClientAsync(progress.ClientId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // AD-22: el progreso se guarda con los BYTES REALES y el total declarado. Un
        // total que no se conoce NO se inventa: se guarda como desconocido.
        //
        // El estado REAL de la máquina que declara el progreso viaja con él, así que la
        // operación se puede CREAR con su tipo y su estado cuando el progreso es la
        // primera noticia que el servidor tiene de esa descarga (AD-10, AD-22).
        _operations.ReportTransfer(
            progress.ClientId,
            progress.OperationId,
            progress.BytesTransferred,
            progress.TotalBytes,
            progress.State);

        _logger.LogInformation(
            "El cliente {ClientId} declara {BytesTransferred} de {TotalBytes} bytes en la operación {OperationId}.",
            progress.ClientId,
            progress.BytesTransferred,
            progress.TotalBytes,
            progress.OperationId);
    }

    /// <summary>
    /// Projects the REAL phase of an installation operation (AD-22, FR-10).
    ///
    /// <para>
    /// El progreso de la instalación se publica SIN porcentaje: el payload del
    /// progreso NO cambia, y un porcentaje sobre un total que no se conoce sería una
    /// mentira (AD-22).
    /// </para>
    /// </summary>
    /// <param name="progress">Progress that the client declares.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the phase has been projected.</returns>
    public async Task ExecuteAsync(
        InstallationProgressUpdated progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!await KnowsClientAsync(progress.ClientId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // AD-10, AD-22: la fase REAL y el estado REAL que declara el cliente se
        // proyectan: el estado viaja con el TEXTO de la tabla ÚNICA de microcopia, así
        // que la tabla del organizador no dice «Pendiente» de una instalación que ya se
        // está instalando (AD-25).
        _operations.ReportPhase(
            progress.ClientId,
            progress.OperationId,
            progress.Phase ?? string.Empty,
            progress.State);

        _logger.LogInformation(
            "El cliente {ClientId} declara la fase '{Phase}' de la instalación {OperationId} en el estado '{State}'.",
            progress.ClientId,
            progress.Phase,
            progress.OperationId,
            progress.State);
    }

    /// <summary>
    /// Tells whether the server knows the client, porque una publicación de un cliente
    /// que NO está registrado no crea ninguna fila (AD-30, AD-32).
    /// </summary>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns><c>true</c> when the server knows the client.</returns>
    private async Task<bool> KnowsClientAsync(string clientId, CancellationToken cancellationToken)
    {
        var existing = await _repository
            .FindByClientIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return true;
        }

        _logger.LogWarning(
            "El cliente {ClientId} publica el progreso de una operación pero no está registrado: la publicación se ignora.",
            clientId);

        return false;
    }
}
