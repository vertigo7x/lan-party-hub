using LANPartyHub.Server.Infrastructure.Signing;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The Ed25519 signature of the installation manifest (AD-2, AD-8).
///
/// <para>
/// The signature is what authenticates the manifest, and it covers the canonical form of
/// it. A signature that verified anything would authenticate nothing, so the guard of the
/// verification is exercised here (AD-2).
/// </para>
/// </summary>
public sealed class ManifestSignerTests
{
    /// <summary>
    /// The signature verifies against the public key of the server (AD-2).
    /// </summary>
    [Fact]
    public void The_signature_verifies_against_the_public_key_of_the_server()
    {
        var signer = new Ed25519ManifestSigner();
        const string canonical = "{\"packageId\":\"abc\",\"schemaVersion\":1}";

        var signature = signer.Sign(canonical);

        Assert.True(Ed25519ManifestSigner.Verify(canonical, signature, Environment.MachineName));
    }

    /// <summary>
    /// A manifest that has been altered does not verify against the signature of the
    /// original one (AD-2, AD-8).
    /// </summary>
    [Fact]
    public void An_altered_manifest_does_not_verify()
    {
        var signer = new Ed25519ManifestSigner();
        const string canonical = "{\"packageId\":\"abc\",\"schemaVersion\":1}";
        const string altered = "{\"packageId\":\"abc\",\"schemaVersion\":2}";

        var signature = signer.Sign(canonical);

        Assert.False(Ed25519ManifestSigner.Verify(altered, signature, Environment.MachineName));
    }

    /// <summary>
    /// The signature of Ed25519 occupies 64 bytes (AD-2).
    /// </summary>
    [Fact]
    public void The_signature_occupies_sixty_four_bytes()
    {
        var signer = new Ed25519ManifestSigner();

        var signature = signer.Sign("{\"packageId\":\"abc\"}");

        Assert.Equal(64, signature.Length / 2);
        Assert.Equal(signature.ToLowerInvariant(), signature);
    }

    /// <summary>
    /// The public key of the server is reproducible from the derivation of its seed, so
    /// that a verifier can obtain the key that signed a manifest (AD-2).
    /// </summary>
    [Fact]
    public void The_public_key_of_the_server_is_reproducible()
    {
        var first = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);
        var second = Ed25519ManifestSigner.ComputePublicKey(Environment.MachineName);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
    }

    /// <summary>
    /// The identifier of the key travels only in the signed envelope of the manifest, and
    /// it is stable for a given host (AD-2, AD-7).
    /// </summary>
    [Fact]
    public void The_identifier_of_the_key_is_stable_for_a_given_host()
    {
        var first = new Ed25519ManifestSigner();
        var second = new Ed25519ManifestSigner();

        Assert.Equal(first.GetKeyId(), second.GetKeyId());
        Assert.False(string.IsNullOrWhiteSpace(first.GetKeyId()));
    }
}
