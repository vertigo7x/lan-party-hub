using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Packages;

/// <summary>
/// Use case that reads the published packages of the event (AD-1, AD-5, AD-7).
///
/// <para>
/// The use case reads the packages through the port of the repository and returns the
/// contract of <c>Shared</c> that the API and the interface publish. It is not a route of
/// the API: the routes that expose the catalog belong to the stories that follow (AD-5).
/// </para>
/// </summary>
public sealed class ListPackages
{
    private readonly IPackageRepository _repository;
    private readonly ILogger<ListPackages> _logger;

    /// <summary>Creates the use case that reads the published packages.</summary>
    /// <param name="repository">Port of the repository of the published packages.</param>
    /// <param name="logger">Log of the use case.</param>
    public ListPackages(IPackageRepository repository, ILogger<ListPackages> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Reads every published package of the event.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The descriptors of the published packages.</returns>
    public async Task<IReadOnlyList<PackageDescriptor>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Se han leído {Count} paquetes publicados del catálogo.",
            rows.Count);

        return rows
            .Select(row => new PackageDescriptor(
                row.PackageId,
                row.GameId,
                row.Version,
                row.SizeBytes,
                row.ChunkSizeBytes ?? 0,
                row.Sha256,
                row.ManifestRef))
            .ToArray();
    }
}
