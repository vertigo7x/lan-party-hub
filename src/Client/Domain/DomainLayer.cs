namespace LANPartyHub.Client.Domain;

/// <summary>
/// Marker type of the Domain layer of the client (AD-1). It holds the entities, the value
/// objects and the state machines, and it never imports an adapter or a third party library.
/// </summary>
public static class DomainLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Domain";
}
