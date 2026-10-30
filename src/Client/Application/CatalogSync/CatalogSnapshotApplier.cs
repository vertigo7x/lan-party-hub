using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.CatalogSync;

/// <summary>
/// Application of a complete snapshot of the catalog in the client (AC1, AC2, AC4, AC5,
/// AC7, AD-26).
///
/// <para>
/// The applier is the ONLY place where the rule of the version lives: a snapshot whose
/// version is greater than the one that the client has applied is applied, and one whose
/// version is equal or older is DISCARDED (AC1, AD-26). The rule lives here and not in
/// the adapter, so the synchronization can be exercised without a server and without a
/// database (AD-1).
/// </para>
/// <para>
/// Applying a snapshot does NOT cancel the operations in course (AC4, AD-26). An
/// operation whose package or whose game was withdrawn from the catalog TERMINATES with
/// the result <see cref="OperationOutcome.PackageWithdrawn"/>, which is a result DISTINCT
/// from the failure: it does not produce an <see cref="ApiError"/>, it is not persisted as
/// a <c>ClientError</c> and it does not add any value to the enumerations of machine state
/// (AD-26, AD-33, R-MVP-8).
/// </para>
/// <para>
/// The installation of a game that was withdrawn from the catalog is NOT deleted (AC5,
/// AD-26): it is conserved, it keeps being shown in the library and it is marked with the
/// derived view state that corresponds to it. The withdrawal of the catalog does not alter
/// the validity of what is already installed.
/// </para>
/// </summary>
public sealed class CatalogSnapshotApplier
{
    private readonly ICatalogSnapshotStore _store;
    private readonly ILogger<CatalogSnapshotApplier> _logger;

    /// <summary>Creates the applier of the snapshot of the catalog.</summary>
    /// <param name="store">Port of the persistence of the snapshot of the catalog.</param>
    /// <param name="logger">Log of the applier.</param>
    public CatalogSnapshotApplier(
        ICatalogSnapshotStore store,
        ILogger<CatalogSnapshotApplier> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Applies the snapshot of the catalog when its version is greater than the one that
    /// the client has applied, and discards it otherwise (AC1, AD-26).
    /// </summary>
    /// <param name="snapshot">Snapshot that the client received.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the application of the snapshot.</returns>
    public async Task<SnapshotApplicationOutcome> ApplyAsync(
        CatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var appliedVersion = await _store
            .GetAppliedVersionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Un snapshot igual o anterior se DESCARTA: la versión del catálogo nunca
        // decrece y nunca se reinicia, de modo que un snapshot viejo no puede degradar
        // el catálogo que el cliente ya tiene (AC1, AD-26).
        if (snapshot.CatalogVersion <= appliedVersion)
        {
            _logger.LogInformation(
                "Se descarta el snapshot del catálogo con la versión {CatalogVersion}: el cliente ya tiene la versión {AppliedVersion}.",
                snapshot.CatalogVersion,
                appliedVersion);

            return SnapshotApplicationOutcome.Discarded(appliedVersion);
        }

        // Las operaciones en curso se leen ANTES de persistir el snapshot, porque la
        // aplicación del snapshot NO las cancela: solo marca el resultado con el que
        // terminan cuando su paquete o su juego fue retirado del catálogo (AC4, AD-26).
        var withdrawnOperations = await ResolveWithdrawnOperationsAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);

        // La instalación de un juego retirado NO se borra: se conserva y se marca con el
        // estado de vista derivado que le corresponde (AC5, AD-26).
        var withdrawnInstallations = await ResolveWithdrawnInstallationsAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);

        await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Se ha aplicado el snapshot del catálogo con la versión {CatalogVersion}: {Games} videojuegos, {Packages} paquetes, {WithdrawnOperations} operaciones retiradas y {WithdrawnInstallations} instalaciones conservadas.",
            snapshot.CatalogVersion,
            snapshot.Games.Count,
            snapshot.Packages.Count,
            withdrawnOperations.Count,
            withdrawnInstallations.Count);

        return new SnapshotApplicationOutcome(
            snapshot.CatalogVersion,
            Applied: true,
            withdrawnOperations,
            withdrawnInstallations);
    }

    /// <summary>
    /// Resolves the operations in course whose package or whose game is no longer in the
    /// catalog (AC4, AC7, AD-26).
    ///
    /// <para>
    /// The operation is NOT cancelled: it is marked with the result
    /// <see cref="OperationOutcome.PackageWithdrawn"/>, which is distinct from the failure
    /// and is never persisted as a <c>ClientError</c> (AD-33, R-MVP-8).
    /// </para>
    /// </summary>
    /// <param name="snapshot">Snapshot that the client applies.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The operations that terminate with the result of the withdrawal.</returns>
    private async Task<IReadOnlyList<WithdrawnOperation>> ResolveWithdrawnOperationsAsync(
        CatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var operations = await _store
            .LoadActiveOperationsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (operations.Count == 0)
        {
            return [];
        }

        // El catálogo aplicado dice qué paquetes siguen publicados y de qué juegos. Un
        // paquete que no está, o un juego que no está, es un retiro del catálogo (AD-26).
        var publishedPackages = snapshot.Packages.ToDictionary(
            package => package.PackageId,
            package => package.GameId,
            StringComparer.Ordinal);

        var publishedGames = snapshot.Games
            .Select(game => game.GameId)
            .ToHashSet(StringComparer.Ordinal);

        var withdrawn = new List<WithdrawnOperation>();

        foreach (var operation in operations)
        {
            // Una operación terminal ya terminó: no hay nada que marcar (AD-10).
            if (DownloadStateMachine.IsTerminalState(operation.State))
            {
                continue;
            }

            var packageIsPublished = publishedPackages.TryGetValue(operation.PackageId, out var gameId);

            if (packageIsPublished && gameId is not null && publishedGames.Contains(gameId))
            {
                continue;
            }

            withdrawn.Add(new WithdrawnOperation(
                operation.OperationId,
                operation.PackageId,
                OperationOutcome.PackageWithdrawn,
                StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)));
        }

        return withdrawn;
    }

    /// <summary>
    /// Resolves the installations of the games that are no longer in the catalog (AC5,
    /// AD-26).
    ///
    /// <para>
    /// The installation is NOT deleted: it is conserved and it keeps the derived view state
    /// that corresponds to it, which the client shows in its library (AD-10, AD-26).
    /// </para>
    /// </summary>
    /// <param name="snapshot">Snapshot that the client applies.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The installations that are conserved with the derived view state.</returns>
    private async Task<IReadOnlyList<WithdrawnInstallation>> ResolveWithdrawnInstallationsAsync(
        CatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var installations = await _store
            .LoadInstalledGamesAsync(cancellationToken)
            .ConfigureAwait(false);

        if (installations.Count == 0)
        {
            return [];
        }

        var publishedGames = snapshot.Games
            .Select(game => game.GameId)
            .ToHashSet(StringComparer.Ordinal);

        var withdrawn = new List<WithdrawnInstallation>();

        foreach (var installation in installations)
        {
            if (publishedGames.Contains(installation.GameId))
            {
                continue;
            }

            // El juego ya no está en el catálogo, pero lo instalado sigue siendo válido:
            // la instalación se conserva y se marca con el estado de vista derivado que
            // le corresponde (AC5, AD-26).
            withdrawn.Add(new WithdrawnInstallation(
                installation.GameId,
                installation.InstalledVersion,
                CatalogViewState.InstalledOutdatedVersion,
                StateDisplayMap.GetText(CatalogViewState.InstalledOutdatedVersion)));
        }

        return withdrawn;
    }
}

/// <summary>
/// The outcome of the application of a snapshot of the catalog (AC1, AD-26).
/// </summary>
/// <param name="CatalogVersion">Version of the catalog that the client has after the application.</param>
/// <param name="Applied">Whether the snapshot was applied or discarded.</param>
/// <param name="WithdrawnOperations">
/// Operations in course that terminate with the result <see cref="OperationOutcome.PackageWithdrawn"/>.
/// </param>
/// <param name="WithdrawnInstallations">
/// Installations of games that were withdrawn from the catalog, conserved with their derived
/// view state.
/// </param>
public sealed record SnapshotApplicationOutcome(
    long CatalogVersion,
    bool Applied,
    IReadOnlyList<WithdrawnOperation> WithdrawnOperations,
    IReadOnlyList<WithdrawnInstallation> WithdrawnInstallations)
{
    /// <summary>
    /// Builds the outcome of a snapshot that was discarded because its version was not
    /// greater than the one that the client already had (AC1).
    /// </summary>
    /// <param name="appliedVersion">Version that the client keeps.</param>
    /// <returns>The outcome of the discarded snapshot.</returns>
    public static SnapshotApplicationOutcome Discarded(long appliedVersion) =>
        new(appliedVersion, Applied: false, [], []);
}

/// <summary>
/// An operation that terminates because its package or its game was withdrawn from the
/// catalog (AC4, AC7, AD-26, AD-33).
/// </summary>
/// <param name="OperationId">Identifier of the operation.</param>
/// <param name="PackageId">Identifier of the package that the operation transfers.</param>
/// <param name="Outcome">Result of the operation: <see cref="OperationOutcome.PackageWithdrawn"/>.</param>
/// <param name="OutcomeText">
/// Text of the result, taken from the single table of microcopy <see cref="StateDisplayMap"/>.
/// </param>
public sealed record WithdrawnOperation(
    string OperationId,
    string PackageId,
    OperationOutcome Outcome,
    string OutcomeText);

/// <summary>
/// An installation that is conserved because its game was withdrawn from the catalog
/// (AC5, AD-26).
/// </summary>
/// <param name="GameId">Identifier of the game.</param>
/// <param name="InstalledVersion">Version of the game that is installed.</param>
/// <param name="ViewState">Derived view state of the installation.</param>
/// <param name="ViewStateText">
/// Text of the derived view state, taken from the single table of microcopy
/// <see cref="StateDisplayMap"/>.
/// </param>
public sealed record WithdrawnInstallation(
    string GameId,
    string InstalledVersion,
    CatalogViewState ViewState,
    string ViewStateText);
