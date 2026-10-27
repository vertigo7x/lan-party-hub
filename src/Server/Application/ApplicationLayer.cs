namespace LANPartyHub.Server.Application;

/// <summary>
/// Marker type of the Application layer of the server (AD-1). The ports of the modules
/// are declared here, and this layer only depends on the Domain layer.
/// </summary>
public static class ApplicationLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Application";
}
