namespace LANPartyHub.Server.Domain;

/// <summary>
/// Marker type of the Domain layer of the server (AD-1). It makes the layer explicit and
/// lets the tests verify that dependencies always point inwards.
/// </summary>
public static class DomainLayer
{
    /// <summary>Name of the layer, used by the structural checks.</summary>
    public const string LayerName = "Domain";
}
