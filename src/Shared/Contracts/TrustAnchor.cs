namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// The trust anchor of the event (AD-2, AD-18, AD-27): the Ed25519 public key of the server
/// plus its fingerprint. The fingerprint is the value the organizer transports out of band;
/// the key is what the client derives the fingerprint from.
/// </summary>
/// <param name="PublicKey">Ed25519 public key of the anchor.</param>
/// <param name="Fingerprint">Stable, human readable fingerprint of the public key.</param>
public sealed record TrustAnchor(string PublicKey, string Fingerprint);
