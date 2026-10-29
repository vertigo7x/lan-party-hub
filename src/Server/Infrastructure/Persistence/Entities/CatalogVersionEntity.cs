namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CATALOG_VERSION</c> table (AD-22, AD-26).
///
/// <para>
/// The table holds a single row: the version of the catalog of the event. The version
/// grows monotonically and it grows in the same transaction as the write that causes it,
/// so a client that compares it with the one that it holds knows whether the snapshot
/// that it receives is newer than the one that it applied (AD-26).
/// </para>
/// <para>
/// The key of the row is the literal <see cref="SingleRowKey"/>, so the table can never
/// hold a second row: the version of the catalog is one, not many.
/// </para>
/// </summary>
public sealed class CatalogVersionEntity
{
    /// <summary>
    /// Key of the single row of the table. The version of the catalog is one, and the
    /// table holds exactly one row with this key.
    /// </summary>
    public const int SingleRowKey = 1;

    /// <summary>Key of the single row of the table.</summary>
    public int Id { get; set; } = SingleRowKey;

    /// <summary>
    /// Version of the catalog of the event. It grows monotonically and it never goes
    /// back (AD-26).
    /// </summary>
    public long CatalogVersion { get; set; }
}
