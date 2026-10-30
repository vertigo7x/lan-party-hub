namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Complete snapshot of the catalog of the event (AD-26, AD-18).
///
/// <para>
/// The catalog is synchronized by complete snapshot and never by delta: the snapshot
/// carries the version of the catalog that produced it, the videogames that are published
/// and the packages that are not withdrawn. The version is monotonically increasing and the
/// client only applies a snapshot whose version is greater than the one it holds; one that
/// is equal or older is discarded (AD-26).
/// </para>
/// <para>
/// The snapshot carries no signature and no data of other clients: the trust of the
/// packages is the one of the signed installation manifest, not the one of the snapshot
/// (AD-2, AD-6).
/// </para>
/// </summary>
/// <param name="CatalogVersion">Monotonically increasing version of the catalog.</param>
/// <param name="Games">Videogames that are published in the catalog.</param>
/// <param name="Packages">Published packages that are not withdrawn from the catalog.</param>
public sealed record CatalogSnapshot(
    long CatalogVersion,
    IReadOnlyList<GameDescriptor> Games,
    IReadOnlyList<PackageDescriptor> Packages);
