using System.Security.Cryptography;
using System.Text;

namespace LANPartyHub.Client.Domain.Trust;

/// <summary>
/// The fingerprint of the trust anchor (AD-2, AD-27).
///
/// <para>
/// AD-27: el fingerprint es el dato que la persona transporta del servidor al cliente por
/// un canal fuera de banda, y el cliente DERIVA la clave pública del ancla a partir de la
/// clave que el servidor presenta y COMPARA su fingerprint con el esperado.
/// </para>
/// <para>
/// La regla del fingerprint es UNA y es la MISMA en el servidor y en el cliente: el
/// <c>sha256</c> de los 32 bytes de la clave pública cruda, en hexadecimal minúsculo. Si
/// las dos unidades aplicaran reglas distintas, un fingerprint auténtico parecería
/// distinto en cada lado y el emparejamiento del evento sería imposible (AD-27).
/// </para>
/// <para>
/// El tipo vive en la capa de Domain porque es una regla del dominio y no un detalle de
/// transporte: el valor se compara, se persiste y se muestra, y ninguno de esos tres actos
/// depende de cómo llegó (AD-1, AD-27).
/// </para>
/// </summary>
public sealed class AnchorFingerprint
{
    /// <summary>
    /// Length of the fingerprint in characters: 32 bytes of <c>sha256</c> in hexadecimal
    /// (AD-27).
    /// </summary>
    public const int Length = 64;

    /// <summary>
    /// Number of bytes of the raw public key of Ed25519 (AD-2, AD-27).
    /// </summary>
    public const int PublicKeyLength = 32;

    private readonly string _value;

    private AnchorFingerprint(string value)
    {
        _value = value;
    }

    /// <summary>
    /// The fingerprint in lowercase hexadecimal (AD-27).
    /// </summary>
    public string Value => _value;

    /// <summary>
    /// Whether the fingerprint is the canonical one: 64 characters of hexadecimal in
    /// lowercase (AD-27).
    /// </summary>
    public bool IsCanonical => _value.Length == Length && IsLowercaseHex(_value);

    /// <summary>
    /// Derives the fingerprint of a raw Ed25519 public key (AD-27).
    ///
    /// <para>
    /// La regla es el <c>sha256</c> de los bytes de la clave, en hexadecimal minúsculo. La
    /// clave se acepta en base64 (que es como el servidor la publica) o en hexadecimal
    /// (que es como puede venir de una herramienta externa), y las dos formas producen el
    /// MISMO fingerprint (AD-27).
    /// </para>
    /// </summary>
    /// <param name="publicKey">Public key of the anchor, in base64 or in hexadecimal.</param>
    /// <returns>The fingerprint of the key.</returns>
    /// <exception cref="FormatException">The key is not a valid Ed25519 public key.</exception>
    public static AnchorFingerprint FromPublicKey(string publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        var raw = DecodePublicKey(publicKey);

        return new AnchorFingerprint(
            Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant());
    }

    /// <summary>
    /// Reads a fingerprint that the player typed or that the QR carried (AD-27).
    ///
    /// <para>
    /// El valor que llega por el canal fuera de banda puede venir con espacios, con
    /// guiones o en mayúsculas, porque el jugador lo transcribe de un cartel o lo teclea
    /// de un QR. La comparación NO PUEDE depender de cómo lo escribió la persona, así que
    /// el valor se normaliza antes de compararlo (AD-27).
    /// </para>
    /// </summary>
    /// <param name="fingerprint">Fingerprint as it was transported.</param>
    /// <returns>The canonical fingerprint.</returns>
    /// <exception cref="FormatException">The value is not a fingerprint.</exception>
    public static AnchorFingerprint Parse(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var normalized = Normalize(fingerprint);

        if (normalized.Length != Length || !IsLowercaseHex(normalized))
        {
            throw new FormatException(
                $"El fingerprint del ancla de confianza debe tener {Length} caracteres hexadecimales.");
        }

        return new AnchorFingerprint(normalized);
    }

    /// <summary>
    /// Tries to read a fingerprint that the player typed or that the QR carried, without
    /// throwing (AD-27).
    /// </summary>
    /// <param name="fingerprint">Fingerprint as it was transported.</param>
    /// <param name="parsed">The canonical fingerprint, when the value is valid.</param>
    /// <returns><c>true</c> when the value is a valid fingerprint.</returns>
    public static bool TryParse(string? fingerprint, out AnchorFingerprint? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        try
        {
            parsed = Parse(fingerprint);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Tells whether two fingerprints are the SAME anchor.
    ///
    /// <para>
    /// La comparación es la del contenido, no la de la referencia: el valor normalizado es
    /// el que se compara, de modo que un fingerprint escrito en mayúsculas y el mismo en
    /// minúsculas son el mismo ancla (AD-27).
    /// </para>
    /// </summary>
    /// <param name="other">Fingerprint to compare with.</param>
    /// <returns><c>true</c> when both fingerprints describe the same anchor.</returns>
    public bool Matches(AnchorFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(_value, other._value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tells whether a fingerprint, as it was transported, is the SAME anchor (AD-27).
    /// </summary>
    /// <param name="fingerprint">Fingerprint as it was transported.</param>
    /// <returns><c>true</c> when the value describes the same anchor.</returns>
    public bool Matches(string? fingerprint) =>
        TryParse(fingerprint, out var parsed) && Matches(parsed!);

    /// <inheritdoc />
    public override string ToString() => _value;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is AnchorFingerprint other && Matches(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode(StringComparison.Ordinal);

    /// <summary>
    /// Normalizes a fingerprint that came by the canal outside of band: it removes the
    /// separators and lowers the case (AD-27).
    /// </summary>
    /// <param name="fingerprint">Fingerprint as it was transported.</param>
    /// <returns>The normalized fingerprint.</returns>
    private static string Normalize(string fingerprint)
    {
        var builder = new StringBuilder(fingerprint.Length);

        foreach (var character in fingerprint)
        {
            if (character is ' ' or '-' or ':' or '_' or '\t' or '\r' or '\n')
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes the public key of the anchor, that the server publishes in base64 (AD-27).
    /// </summary>
    /// <param name="publicKey">Public key of the anchor.</param>
    /// <returns>The raw bytes of the key.</returns>
    /// <exception cref="FormatException">The key is not a valid Ed25519 public key.</exception>
    private static byte[] DecodePublicKey(string publicKey)
    {
        var trimmed = publicKey.Trim();

        byte[] raw;

        try
        {
            raw = Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            // La clave puede venir en hexadecimal, que es como la muestran algunas
            // herramientas de la organización.
            try
            {
                raw = Convert.FromHexString(trimmed);
            }
            catch (FormatException exception)
            {
                throw new FormatException(
                    "La clave pública del ancla de confianza no es ni base64 ni hexadecimal.",
                    exception);
            }
        }

        if (raw.Length != PublicKeyLength)
        {
            throw new FormatException(
                $"La clave pública del ancla de confianza tiene que tener {PublicKeyLength} bytes.");
        }

        return raw;
    }

    /// <summary>
    /// Tells whether a text is hexadecimal in lowercase (AD-27).
    /// </summary>
    /// <param name="value">Text to check.</param>
    /// <returns><c>true</c> when the text is lowercase hexadecimal.</returns>
    private static bool IsLowercaseHex(string value)
    {
        foreach (var character in value)
        {
            var isDigit = character is >= '0' and <= '9';
            var isLowerHex = character is >= 'a' and <= 'f';

            if (!isDigit && !isLowerHex)
            {
                return false;
            }
        }

        return true;
    }
}
