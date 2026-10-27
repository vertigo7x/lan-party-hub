namespace LANPartyHub.Client.Infrastructure;

/// <summary>
/// Marker type of the Infrastructure layer of the client (AD-1). The adapters live here:
/// EF Core over SQLite, HTTP, SignalR, mDNS and the file system.
/// </summary>
public static class InfrastructureLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Infrastructure";
}
