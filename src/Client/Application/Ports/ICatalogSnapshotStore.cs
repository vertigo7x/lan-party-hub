using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the persistence of the snapshot of the catalog of the client (AD-11, AD-22,
/// AD-26).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// owns the file <c>state.sqlite</c> of the client. The snapshot survives to the restart of
/// the client, so a player that starts the client without the server still sees the last
/// catalog that was synchronized (AC3, AC-5).
/// </para>
/// <para>
/// The table of the snapshot belongs to the client and it never replicates a table of the
/// server (AD-22).
/// </para>
/// </summary>
public interface ICatalogSnapshotStore
{
    /// <summary>
    /// Reads the snapshot that the client has applied, or <c>null</c> when the client has
    /// never applied one (AC1, AC3).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The snapshot applied, or <c>null</c>.</returns>
    Task<CatalogSnapshot?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the version of the catalog that the client has applied, or zero when it has
    /// never applied one (AC1, AD-26).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The version applied.</returns>
    Task<long> GetAppliedVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the instant of the last successful synchronization of the catalog, or
    /// <c>null</c> when the client has never synchronized (AC3, UX-DR-32).
    ///
    /// <para>
    /// The instant is the one that the notice of the pending synchronization shows to the
    /// player: «Catálogo desactualizado · sincronizado hace 12 min» (AC3).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The instant of the last successful synchronization.</returns>
    Task<DateTimeOffset?> GetLastSynchronizedAtAsync(CancellationToken cancellationToken);

/// <summary>
    /// Persists the snapshot that the client applies (AC2, AD-26).
    ///
    /// <para>
    /// The write happens inside a transaction, so the version and the payload that travel
    /// together are the ones of the same instant (AD-26). The adapter records the instant
    /// of the synchronization, which is what the notice of the pending synchronization
    /// shows to the player (AC3, UX-DR-32).
    /// </para>
    /// </summary>
    /// <param name="snapshot">Snapshot that the client applies.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the snapshot has been persisted.</returns>
    Task SaveAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the games that the client has installed (AC5, AD-26).
    ///
    /// <para>
    /// The reading exists so that the application of a snapshot can mark the installation
    /// of a game that was withdrawn WITHOUT deleting it: the withdrawal of the catalog does
    /// not alter the validity of what is already installed (AD-26).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The identifiers of the installed games and their installed version.</returns>
    Task<IReadOnlyList<InstalledGame>> LoadInstalledGamesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the operations of download that the client has in course (AC4, AD-26).
    ///
    /// <para>
    /// The reading exists so that the application of a snapshot can mark the operation
    /// whose package or game was withdrawn with the result <see cref="OperationOutcome.PackageWithdrawn"/>:
    /// the snapshot does NOT cancel the operation in course, and the result is distinct from
    /// the failure (AD-26, AD-33).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The operations in course with the package that they transfer.</returns>
    Task<IReadOnlyList<ActiveOperation>> LoadActiveOperationsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A game that the client has installed (AD-22, AD-26).
/// </summary>
/// <param name="GameId">Identifier of the game.</param>
/// <param name="InstalledVersion">Version of the game that is installed.</param>
/// <param name="ValidationState">State of the installation.</param>
public sealed record InstalledGame(
    string GameId,
    string InstalledVersion,
    InstallationState ValidationState);

/// <summary>
/// An operation of download that the client has in course (AD-22, AD-26, AD-33).
/// </summary>
/// <param name="OperationId">Identifier of the operation.</param>
/// <param name="PackageId">Identifier of the package that the operation transfers.</param>
/// <param name="State">State of the operation.</param>
public sealed record ActiveOperation(
    string OperationId,
    string PackageId,
    DownloadState State);
