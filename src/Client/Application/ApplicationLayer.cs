namespace LANPartyHub.Client.Application;

/// <summary>
/// Marker type of the Application layer of the client (AD-1). The use cases live here and
/// they depend on the ports declared in this layer, never on an adapter.
/// </summary>
public static class ApplicationLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Application";
}
