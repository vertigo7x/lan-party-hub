namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Marker type of the Presentation layer of the client (AD-1). The Avalonia view models live
/// here, they consume the use cases of the client and they hold no business rule.
/// </summary>
public static class PresentationLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Presentation";
}
