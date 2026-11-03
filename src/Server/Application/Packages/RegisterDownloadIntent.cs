using LANPartyHub.Server.Application.Ports;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Packages;

/// <summary>
/// Use case that records the intention of a download (AD-10, AD-22, AC1).
///
/// <para>
/// El cliente registra su intención con <c>POST /api/v1/downloads</c> y obtiene el
/// identificador de la operación. El servidor guarda esa intención y NADA del estado de la
/// operación: la descarga la ejecuta el cliente y el estado de la máquina es del cliente
/// (AD-10, AD-22).
/// </para>
/// <para>
/// La fila que el servidor escribe es el REFLEJO de la intención: el identificador de la
/// operación, el identificador del cliente, el identificador del paquete y el estado
/// inicial <c>Queued</c>. El resultado terminal se queda VACÍO porque la operación
/// todavía no terminó (AD-10, AD-33).
/// </para>
/// </summary>
public sealed class RegisterDownloadIntent
{
    private readonly IDownloadIntentRepository _repository;
    private readonly IPackageRepository _packages;
    private readonly ILogger<RegisterDownloadIntent> _logger;

    /// <summary>Creates the use case that records the intention of a download.</summary>
    /// <param name="repository">Port of the registry of the download intentions.</param>
    /// <param name="packages">Port of the repository of the published packages.</param>
    /// <param name="logger">Log of the use case.</param>
    public RegisterDownloadIntent(
        IDownloadIntentRepository repository,
        IPackageRepository packages,
        ILogger<RegisterDownloadIntent> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _packages = packages;
        _logger = logger;
    }

    /// <summary>
    /// Records the intention of downloading a package and returns the identifier of the
    /// operation.
    /// </summary>
    /// <param name="clientId">Opaque identifier of the client that asks for the download.</param>
    /// <param name="packageId">Opaque identifier of the package that the client wants.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The identifier of the registered operation.</returns>
    /// <exception cref="DownloadPackageNotFoundException">
    /// The package is not published in the catalog of the event.
    /// </exception>
    public async Task<string> ExecuteAsync(
        string clientId,
        string packageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // La intención solo se registra para un paquete PUBLICADO: el servidor no sirve
        // bytes de un paquete que no está en el catálogo, así que una intención sobre un
        // paquete que no existe no puede llegar a.transferirse (AD-7, AD-22).
        var descriptor = await _packages
            .FindAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        if (descriptor is null)
        {
            throw new DownloadPackageNotFoundException(packageId);
        }

        var operationId = Guid.CreateVersion7().ToString("N");

        // AD-10, AD-22: la fila del servidor es el reflejo de la INTENCIÓN. El estado que
        // se guarda es el inicial de la máquina y el resultado terminal se deja vacío
        // porque la operación todavía no terminó.
        await _repository.AddAsync(
            new DownloadIntentRow(
                operationId,
                clientId,
                packageId,
                DownloadIntentStates.Queued,
                string.Empty),
            cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Se ha registrado la intención de descarga de la operación {OperationId} del cliente {ClientId} sobre el paquete {PackageId}.",
            operationId,
            clientId,
            packageId);

        return operationId;
    }
}

/// <summary>
/// Names of the state and the outcome of a download operation, as the server persists
/// them (AD-10, AD-22, AD-33).
///
/// <para>
/// Son TEXTO y no el enum de <c>Shared</c>: el servidor no es el dueño del estado de la
/// máquina del cliente, y lo que guarda es el reflejo de lo que el cliente le dijo
/// (AD-22).
/// </para>
/// </summary>
public static class DownloadIntentStates
{
    /// <summary>State that every registered operation starts in (AD-10).</summary>
    public const string Queued = "Queued";
}

/// <summary>
/// The package of a download intention is not published in the catalog of the event
/// (AD-7, AD-22).
/// </summary>
/// <param name="PackageId">Opaque identifier of the package that does not exist.</param>
public sealed class DownloadPackageNotFoundException : Exception
{
    /// <summary>Creates the exception of a package that is not published.</summary>
    /// <param name="packageId">Opaque identifier of the package that does not exist.</param>
    public DownloadPackageNotFoundException(string packageId)
        : base($"El paquete '{packageId}' no está publicado en el catálogo del evento.")
    {
        PackageId = packageId;
    }

    /// <summary>Opaque identifier of the package that does not exist.</summary>
    public string PackageId { get; }
}
