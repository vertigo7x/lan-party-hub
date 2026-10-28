using LANPartyHub.Client.Domain;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Access to the database of the client (AD-11, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that owns
/// EF Core and the SQLite file. The schema of that file belongs to the client and it never
/// replicates a table of the server.
/// </para>
/// </summary>
public interface IClientStateStore
{
    /// <summary>
    /// Applies the versioned migrations of the database of the client. The migrations are
    /// applied at startup and <c>EnsureCreated</c> is never used (AD-11).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the database is up to date.</returns>
    Task MigrateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the identity of the client, or <c>null</c> when the client has not emitted one
    /// yet (AD-23).
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the identity of the client.</returns>
    Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the identity of the client. The identifier is written once and it is never
    /// regenerated (AD-23).
    /// </summary>
    /// <param name="identity">Identity of the client.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the identity is persisted.</returns>
    Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the persisted download operations with the progress that goes with them.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the persisted operations.</returns>
    Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the accumulated offset of a download operation (AD-11, AD-24).
    /// </summary>
    /// <param name="operationId">Opaque identifier of the operation.</param>
    /// <param name="bytesPersisted">Accumulated offset in bytes.</param>
    /// <param name="totalBytes">Total bytes of the package, or <c>null</c> when they are unknown and the persisted ones have to be kept.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the progress is persisted.</returns>
    Task SaveProgressAsync(string operationId, long bytesPersisted, long? totalBytes, CancellationToken cancellationToken);
}
