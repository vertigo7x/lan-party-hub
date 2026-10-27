namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Derived view states of the catalog (AD-10, AD-18). They are not machine states:
/// they are projections computed from the catalog snapshot and the local installations.
/// </summary>
public enum CatalogViewState
{
    /// <summary>The client holds an outdated catalog snapshot, or is in its first synchronization.</summary>
    Synchronizing,

    /// <summary>The game is published in the catalog and the client has no valid installation for it.</summary>
    Available,

    /// <summary>
    /// There is a validated installation whose version is older than the version published
    /// in the catalog for the same game.
    /// </summary>
    InstalledOutdatedVersion,
}
