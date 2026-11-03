namespace LANPartyHub.Client.Domain.Trust;

/// <summary>
/// The ancla de confianza that the client has FIXED (AD-27).
///
/// <para>
/// AD-27: el ancla fijada es INMUTABLE durante el evento. Una vez persistida, el cliente
/// no la sustituye ni acepta otra clave ante una discrepancia posterior: la rechaza y
/// exige un emparejamiento nuevo, explícito y fuera de banda.
/// </para>
/// <para>
/// El tipo lleva el fingerprint CANÓNICO y la clave pública en base64, que es como el
/// servidor la publica. La clave es lo que valida las firmas de los manifiestos y el
/// fingerprint es el valor con el que el jugador contrasta (AD-2, AD-27).
/// </para>
/// </summary>
public sealed record PinnedTrustAnchor
{
    /// <summary>
    /// Creates the ancla de confianza that the client has fixed.
    /// </summary>
    /// <param name="publicKey">Public key of the anchor, in base64.</param>
    /// <param name="fingerprint">Canonical fingerprint of the public key.</param>
    /// <param name="fixedAt">Instant of the fixation, in UTC ISO-8601.</param>
    public PinnedTrustAnchor(string publicKey, string fingerprint, string fixedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedAt);

        PublicKey = publicKey;
        Fingerprint = fingerprint;
        FixedAt = fixedAt;
    }

    /// <summary>Public key of the anchor, in base64.</summary>
    public string PublicKey { get; }

    /// <summary>Canonical fingerprint of the public key, in lowercase hexadecimal.</summary>
    public string Fingerprint { get; }

    /// <summary>Instant of the fixation, in UTC ISO-8601.</summary>
    public string FixedAt { get; }

    /// <summary>
    /// The fingerprint of this ancla as an object of the domain (AD-27).
    /// </summary>
    public AnchorFingerprint AnchorFingerprint => AnchorFingerprint.Parse(Fingerprint);
}
