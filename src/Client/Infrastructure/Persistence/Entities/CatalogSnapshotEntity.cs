namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>CATALOG_SNAPSHOT</c> table (AD-11, AD-22, AD-26).
///
/// <para>
/// The table holds a SINGLE row: the complete snapshot of the catalog that the client has
/// applied, with the version that produced it and the instant of the synchronization. The
/// snapshot survives the restart of the client, so a player that starts the client without
/// the server still sees the last catalog that was synchronized (AC3).
/// </para>
/// <para>
/// The payload is stored as its canonical JSON text, so the version and the payload that
/// travel together are exactly the ones that the server published (AD-26).
/// </para>
/// </summary>
public sealed class CatalogSnapshotEntity
{
    /// <summary>
    /// Key of the single row of the table. It is the same convention that the table of the
    /// version of the catalog of the server uses (AD-26).
    /// </summary>
    public const int SingleRowKey = 1;

    /// <summary>Key of the single row of the table.</summary>
    public int Id { get; set; } = SingleRowKey;

    /// <summary>Version of the catalog that the client has applied.</summary>
    public long CatalogVersion { get; set; }

    /// <summary>Instant of the last successful synchronization, in UTC ISO-8601.</summary>
    public string SynchronizedAt { get; set; } = string.Empty;

    /// <summary>Canonical JSON text of the complete snapshot of the catalog.</summary>
    public string Payload { get; set; } = string.Empty;
}
