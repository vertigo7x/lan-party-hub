namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Versions of the system (AD-18, AD-6). It is the origin of the version headers exchanged
/// by the client and the server.
/// </summary>
/// <param name="ClientVersion">Version of the client binary.</param>
/// <param name="ServerVersion">Version of the server binary.</param>
/// <param name="ProtocolVersion">Protocol version spoken by both sides.</param>
public sealed record VersionInfo(string ClientVersion, string ServerVersion, int ProtocolVersion)
{
    /// <summary>
    /// The trust anchor that the server publishes, or <c>null</c> when it does not publish
    /// one (AD-27).
    ///
    /// <para>
    /// AD-27: el servidor publica su ancla <b>para contrastarla</b>, nunca para aceptarla.
    /// El cliente deriva el fingerprint de la clave pública que llega y lo compara con el
    /// que Ya tiene, y solo persiste el ancla cuando la comparación la confirma el
    /// jugador. Un valor que llega por la LAN no puede convertirse en ancla por sí mismo.
    /// </para>
    /// <para>
    /// La propiedad es OPCIONAL y el inventario de los contratos de <c>Shared</c> no cambia:
    /// sigue siendo el mismo tipo <see cref="TrustAnchor"/>, el que ya existía (AD-18,
    /// AD-33).
    /// </para>
    /// </summary>
    public TrustAnchor? Anchor { get; init; }

    /// <summary>
    /// The protocol version that the current build speaks (AD-6).
    ///
    /// <para>
    /// It is the SINGLE origin of the version of the protocol of the system: the client
    /// declares it in every request and the server compares the declared version against
    /// the range that it supports. It is a constant of this contract and NOT a type, so
    /// the inventory of the contracts of <c>Shared</c> does not change (AD-18, AD-33).
    /// </para>
    /// </summary>
    public const int CurrentProtocolVersion = 3;
}
