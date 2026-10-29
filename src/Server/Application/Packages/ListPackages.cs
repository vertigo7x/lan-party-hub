using LANPartyHub.Server.Application.Ports;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Packages;

/// <summary>
/// Use case that reads the published packages of the event (AD-1, AD-5, AD-7, AC1).
///
/// <para>
/// The use case reads the packages through the port of the repository and returns the
/// enriched row of the server, the one that carries the videogame, the exact size, the
/// license, the restrictions of distribution and the state of the withdrawal (AC1). It is
/// NOT a contract of <c>Shared</c>: the twenty contracts of <c>Shared</c> are fixed and
/// the server does not redeclare any of them (AD-18).
/// </para>
/// <para>
/// The use case is not a route of the API: the routes that expose the catalog belong to
/// the stories that follow (AD-5).
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
    /// Reads every published package of the event, enriched with the metadata of the
    /// videogame that owns it.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The enriched rows of the published packages.</returns>
    public async Task<IReadOnlyList<PublishedPackageRow>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var rows = await _repository.ListPublishedAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Se han leído {Count} paquetes publicados del catálogo.",
            rows.Count);

        return rows;
    }
}
