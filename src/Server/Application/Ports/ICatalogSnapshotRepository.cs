using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the reading of the complete snapshot of the catalog (AD-1, AD-26, AD-22).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// reads the SQLite file of the server. The reading happens with a consistent view of the
/// catalog: the version, the videogames and the packages are read as one snapshot, so a
/// client never receives a version that announces a publication that is not in the payload
/// (AD-26).
/// </para>
/// <para>
/// The catalog is owned by the server and it lives in <c>catalog.sqlite</c>. No module of
/// the server reads the table of another module (AD-22).
/// </para>
/// </summary>
public interface ICatalogSnapshotRepository
{
    /// <summary>
    /// Reads the complete snapshot of the catalog: the version, the videogames that are
    /// published and the packages that are not withdrawn (AC1, AC2, AD-26).
    ///
    /// <para>
    /// The withdrawal MARKS and never deletes (AD-26): a withdrawn package is not part of
    /// the snapshot, but its row survives in the file of the server, so the installation
    /// that a client already made is not invalidated (AC5).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The complete snapshot of the catalog.</returns>
    Task<CatalogSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}
