namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Trigger sent by the server to the client when the catalog changed (AD-5, AD-18).
/// It is the only server to client event about the catalog.
/// </summary>
/// <param name="CatalogVersion">Monotonically increasing version of the catalog snapshot.</param>
public sealed record CatalogChanged(long CatalogVersion);
