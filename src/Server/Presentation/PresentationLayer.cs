namespace LANPartyHub.Server.Presentation;

/// <summary>
/// Marker type of the Presentation layer of the server (AD-1). The Avalonia windows and
/// their view models live here, they consume the use cases of the server and they hold no
/// business rule.
/// </summary>
public static class PresentationLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Presentation";
}
