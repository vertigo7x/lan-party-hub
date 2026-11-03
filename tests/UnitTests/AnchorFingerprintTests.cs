using LANPartyHub.Client.Domain.Trust;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The fingerprint of the trust anchor (AD-27).
///
/// <para>
/// AD-27: la regla del fingerprint es UNA y es la MISMA en el servidor y en el cliente.
/// El test del VECTOR DORADO fija el valor con el que el servidor y el cliente tienen que
/// coincidir, de modo que una regla que cambie en cualquiera de los dos lados rompe este
/// test en lugar de romper el emparejamiento del evento en el silencio (AD-27).
/// </para>
/// </summary>
public sealed class AnchorFingerprintTests
{
    /// <summary>
    /// Machine name whose derived key is the golden vector (AD-27).
    /// </summary>
    public const string GoldenMachineName = "golden-vector-host";

    /// <summary>
    /// Public key of the golden vector, in hexadecimal (AD-27).
    /// </summary>
    public const string GoldenPublicKeyHex =
        "efe30018947cb8c72e53913ae59543357425307dcea983290722624ac4cca120";

    /// <summary>
    /// Public key of the golden vector, in base64, which is how the server publishes it
    /// (AD-27).
    /// </summary>
    public const string GoldenPublicKeyBase64 =
        "7+MAGJR8uMcuU5E65ZVDNXQlMH3OqYMpByJiSsTMoSA=";

    /// <summary>
    /// Fingerprint of the golden vector, in lowercase hexadecimal (AD-27).
    /// </summary>
    public const string GoldenFingerprint =
        "19bb47ff53da69b4b8bd88d58d9bef792e3f5d40e742c9b60316050dad3a5889";

    [Fact]
    public void The_fingerprint_of_the_golden_vector_is_the_agreed_value()
    {
        // AD-27: el servidor y el cliente aplican la MISMA regla, así que el valor del
        // vector dorado no puede depender de quién lo calcula.
        var fingerprint = AnchorFingerprint.FromPublicKey(GoldenPublicKeyBase64);

        Assert.Equal(GoldenFingerprint, fingerprint.Value);
    }

    [Fact]
    public void The_public_key_in_base64_and_in_hexadecimal_give_the_same_fingerprint()
    {
        // El servidor publica la clave en base64 y la organización puede teclearla en
        // hexadecimal: las dos formas tienen que dar el mismo fingerprint (AD-27).
        var fromBase64 = AnchorFingerprint.FromPublicKey(GoldenPublicKeyBase64);
        var fromHex = AnchorFingerprint.FromPublicKey(GoldenPublicKeyHex);

        Assert.Equal(fromBase64.Value, fromHex.Value);
    }

    [Fact]
    public void The_fingerprint_is_sixty_four_lowercase_hexadecimal_characters()
    {
        var fingerprint = AnchorFingerprint.FromPublicKey(GoldenPublicKeyBase64);

        Assert.Equal(64, fingerprint.Value.Length);
        Assert.True(fingerprint.IsCanonical);
        Assert.Equal(fingerprint.Value.ToLowerInvariant(), fingerprint.Value);
    }

    [Fact]
    public void A_fingerprint_transported_with_spaces_keeps_the_same_value()
    {
        // AD-27: el jugador transcribe el fingerprint de un cartel o lo teclea de un QR, así
        // que el valor llega con espacios y guiones. La comparación NO puede depender de
        // cómo lo escribió la persona (AD-27).
        var grouped = "19bb 47ff 53da 69b4 b8bd 88d5 9d9b ef79 2e3f 5d40 e742 c9b6 0316 050d ad3a 5889";

        var parsed = AnchorFingerprint.Parse(grouped);

        Assert.Equal(GoldenFingerprint, parsed.Value);
    }

    [Fact]
    public void A_fingerprint_transported_in_uppercase_keeps_the_same_value()
    {
        var parsed = AnchorFingerprint.Parse(GoldenFingerprint.ToUpperInvariant());

        Assert.Equal(GoldenFingerprint, parsed.Value);
    }

    [Fact]
    public void A_fingerprint_with_dashes_keeps_the_same_value()
    {
        var dashed = GoldenFingerprint.Chunk(8).Select(chunk => string.Join('-', chunk));

        var parsed = AnchorFingerprint.Parse(string.Join('-', dashed));

        Assert.Equal(GoldenFingerprint, parsed.Value);
    }

    [Fact]
    public void A_fingerprint_that_is_not_a_fingerprint_is_rejected()
    {
        Assert.Throws<FormatException>(() => AnchorFingerprint.Parse("no-es-un-fingerprint"));
    }

    [Fact]
    public void A_fingerprint_that_is_too_short_is_rejected()
    {
        Assert.Throws<FormatException>(() => AnchorFingerprint.Parse(GoldenFingerprint[..32]));
    }

    [Fact]
    public void A_fingerprint_that_is_too_long_is_rejected()
    {
        Assert.Throws<FormatException>(() => AnchorFingerprint.Parse(GoldenFingerprint + "00"));
    }

    [Fact]
    public void A_fingerprint_that_is_not_hexadecimal_is_rejected()
    {
        var notHex = "zzbb47ff53da69b4b8bd88d58d9bef792e3f5d40e742c9b60316050dad3a5889";

        Assert.Throws<FormatException>(() => AnchorFingerprint.Parse(notHex));
    }

    [Fact]
    public void Trying_to_parse_an_invalid_value_does_not_throw()
    {
        AnchorFingerprint? parsed = null;

        Assert.False(AnchorFingerprint.TryParse("nada", out parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Trying_to_parse_an_empty_value_does_not_throw()
    {
        AnchorFingerprint? parsed = null;

        Assert.False(AnchorFingerprint.TryParse("   ", out parsed));
        Assert.False(AnchorFingerprint.TryParse(null, out parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Two_fingerprints_of_the_same_key_match()
    {
        var first = AnchorFingerprint.FromPublicKey(GoldenPublicKeyBase64);
        var second = AnchorFingerprint.Parse(GoldenFingerprint);

        Assert.True(first.Matches(second));
        Assert.True(first.Matches(GoldenFingerprint));
        Assert.True(first.Matches(GoldenFingerprint.ToUpperInvariant()));
    }

    [Fact]
    public void Two_fingerprints_of_different_keys_do_not_match()
    {
        var first = AnchorFingerprint.FromPublicKey(GoldenPublicKeyBase64);

        var other = AnchorFingerprint.Parse(
            "0000000000000000000000000000000000000000000000000000000000000000");

        Assert.False(first.Matches(other));
        Assert.False(first.Matches("0000000000000000000000000000000000000000000000000000000000000000"));
    }

    [Fact]
    public void A_public_key_that_is_not_thirty_two_bytes_is_rejected()
    {
        // La clave del ancla de Ed25519 tiene 32 bytes: una clave de otro tamaño no es una
        // clave de ancla (AD-2, AD-27).
        var tooShort = Convert.ToBase64String(new byte[16]);

        Assert.Throws<FormatException>(() => AnchorFingerprint.FromPublicKey(tooShort));
    }

    [Fact]
    public void A_public_key_that_is_neither_base64_nor_hexadecimal_is_rejected()
    {
        Assert.Throws<FormatException>(() => AnchorFingerprint.FromPublicKey("no-es-una-clave"));
    }
}
