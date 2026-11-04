using LANPartyHub.Client.Infrastructure.Trust;
using LANPartyHub.Server.Infrastructure.Signing;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The verification of the Ed25519 signature of the installation manifest in the client
/// (AD-2, AD-8, AC3).
///
/// <para>
/// La firma es lo que autentica el manifiesto. Un verificador que aceptara cualquier
/// firma no autenticaría nada, así que el guard se ejercita aquí: la firma válida, la firma
/// de OTRA clave, el manifiesto alterado y la firma que no se puede leer (AD-2, AD-8).
/// </para>
/// </summary>
public sealed class Ed25519ManifestVerifierTests
{
    private const string CanonicalManifest =
        "{\"launch\":{\"arguments\":[],\"executablePath\":\"g.exe\",\"workingDirectory\":\"g\"},"
        + "\"operations\":[],\"packageId\":\"paquete-1\",\"packageVersion\":\"1.0.0\","
        + "\"schemaVersion\":1,\"sha256\":\"abc\",\"sizeBytes\":10}";

    /// <summary>
    /// The signature of the server verifies against the public key of the server
    /// (AD-2).
    /// </summary>
    [Fact]
    public void The_signature_of_the_server_verifies_against_its_public_key()
    {
        var signer = new Ed25519ManifestSigner();
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        var signature = signer.Sign(CanonicalManifest);

        var verifier = new Ed25519ManifestVerifier();

        Assert.True(verifier.Verify(CanonicalManifest, signature, publicKey));
    }

    /// <summary>
    /// A manifest that has been ALTERED does not verify against the signature of the
    /// original one (AD-2, AD-8).
    /// </summary>
    [Fact]
    public void An_altered_manifest_does_not_verify()
    {
        var signer = new Ed25519ManifestSigner();
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        var signature = signer.Sign(CanonicalManifest);

        const string altered = "{\"packageId\":\"paquete-2\",\"schemaVersion\":1}";

        var verifier = new Ed25519ManifestVerifier();

        Assert.False(verifier.Verify(altered, signature, publicKey));
    }

    /// <summary>
    /// A signature of ANOTHER key does not verify against the key of the anchor
    /// (AD-2, AD-27).
    ///
    /// <para>
    /// Este es el caso de la SUPLANTACIÓN: el manifiesto llega firmado con una clave que
    /// no es la del ancla fijada, así que el cliente lo rechaza como NO CONFIABLE (AD-27).
    /// </para>
    /// </summary>
    [Fact]
    public void A_signature_of_another_key_does_not_verify()
    {
        // La clave del ancla es la del servidor, y la firma la produce OTRA clave: la
        // del servidor de otro equipo.
        var anchorKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);
        var otherSigner = new Ed25519ManifestSigner("otro-equipo-del-evento");

        var signature = otherSigner.Sign(CanonicalManifest);

        var verifier = new Ed25519ManifestVerifier();

        Assert.False(verifier.Verify(CanonicalManifest, signature, anchorKey));
    }

    /// <summary>
    /// A signature that is not hexadecimal does not verify, and the verification does not
    /// throw (AD-15).
    /// </summary>
    [Theory]
    [InlineData("no es hexadecimal")]
    [InlineData("zzzz")]
    [InlineData("")]
    public void A_signature_that_cannot_be_read_does_not_verify(string signature)
    {
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        var verifier = new Ed25519ManifestVerifier();

        Assert.False(verifier.Verify(CanonicalManifest, signature, publicKey));
    }

    /// <summary>
    /// A signature of the wrong LENGTH does not verify, y no lanza (AD-15).
    /// </summary>
    [Fact]
    public void A_signature_of_the_wrong_length_does_not_verify()
    {
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        var verifier = new Ed25519ManifestVerifier();

        // Una firma de Ed25519 ocupa 64 bytes, así que 32 bytes no es una firma.
        Assert.False(verifier.Verify(CanonicalManifest, new string('a', 64), publicKey));
    }

    /// <summary>
    /// A public key of the wrong LENGTH does not verify, y no lanza (AD-15).
    /// </summary>
    [Fact]
    public void A_public_key_of_the_wrong_length_does_not_verify()
    {
        var signer = new Ed25519ManifestSigner();
        var signature = signer.Sign(CanonicalManifest);

        var verifier = new Ed25519ManifestVerifier();

        Assert.False(verifier.Verify(CanonicalManifest, signature, new byte[16]));
    }

    /// <summary>
    /// The canonical text is verified EXACTLY as it arrives: los bytes que se verifican
    /// son los que la firma cubre (AD-2).
    /// </summary>
    [Fact]
    public void The_canonical_text_is_verified_exactly_as_it_arrives()
    {
        var signer = new Ed25519ManifestSigner();
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        var signature = signer.Sign(CanonicalManifest);

        var verifier = new Ed25519ManifestVerifier();

        // Un espacio de más cambia los bytes, así que la firma deja de validar: el
        // verificador NO normaliza el texto, porque los bytes firmados son los que
        // llegaron (AD-2).
        Assert.False(verifier.Verify($"{CanonicalManifest} ", signature, publicKey));
    }

    /// <summary>
    /// The verifier is deterministic: la misma firma sobre el mismo texto da el mismo
    /// veredicto (AD-2).
    /// </summary>
    [Fact]
    public void The_verifier_is_deterministic()
    {
        var signer = new Ed25519ManifestSigner();
        var publicKey = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        var signature = signer.Sign(CanonicalManifest);

        var verifier = new Ed25519ManifestVerifier();

        Assert.Equal(
            verifier.Verify(CanonicalManifest, signature, publicKey),
            verifier.Verify(CanonicalManifest, signature, publicKey));
    }
}
