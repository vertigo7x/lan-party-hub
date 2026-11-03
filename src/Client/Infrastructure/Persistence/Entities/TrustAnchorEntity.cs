namespace LANPartyHub.Client.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>TRUST_ANCHOR</c> table (AD-11, AD-22, AD-27).
///
/// <para>
/// The table holds a SINGLE row: the ancla de confianza that the client FIXED outside of
/// band, with the fingerprint that was confirmed and the instant of the fixation.
/// </para>
/// <para>
/// AD-27: el ancla fijada es INMUTABLE durante el evento. Una vez escrita, la fila no se
/// sustituye por otra clave: el cliente la rechaza y exige un emparejamiento nuevo, fuera
/// de banda.
/// </para>
/// <para>
/// La tabla no figura en el ERD de la espina, igual que la del snapshot del catálogo, que
/// es el precedente que la historia 3.x fijó (AD-11, AD-27).
/// </para>
/// </summary>
public sealed class TrustAnchorEntity
{
    /// <summary>
    /// Key of the single row of the table. It is the same convention that the table of the
    /// snapshot of the catalog uses (AD-26).
    /// </summary>
    public const int SingleRowKey = 1;

    /// <summary>Key of the single row of the table.</summary>
    public int Id { get; set; } = SingleRowKey;

    /// <summary>Public key of the ancla de confianza, in base64.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Canonical fingerprint of the public key, in lowercase hexadecimal.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Instant of the fixation, in UTC ISO-8601.</summary>
    public string FixedAt { get; set; } = string.Empty;
}
