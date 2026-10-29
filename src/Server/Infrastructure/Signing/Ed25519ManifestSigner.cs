using System.Security.Cryptography;
using System.Text;
using LANPartyHub.Server.Application.Ports;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace LANPartyHub.Server.Infrastructure.Signing;

/// <summary>
/// Adapter of the signature of the installation manifest with Ed25519 (AD-2, AD-8).
///
/// <para>
/// The signature is <b>Ed25519</b> over the <em>canonical form</em> of the manifest: the
/// canonical JSON text with the keys sorted by name and without any superfluous whitespace.
/// The text that this adapter signs is the very text that the use case persists in the
/// column <c>canonicalManifest</c>, so a verifier recomputes exactly the same bytes and
/// the signature covers them (AD-2).
/// </para>
/// <para>
/// The private key of the server lives in this adapter and it never leaves it: the use case
/// knows only the port <see cref="IManifestSigner"/>, and no type of this cryptography
/// crosses the boundary of this file (AD-1).
/// </para>
/// <para>
/// The seed of the key is derived deterministically, so that the public key of a running
/// server can be reproduced by anyone that knows the derivation, and so that the integration
/// level can verify a persisted signature against the very key that produced it. The
/// derivation is: the UTF-8 bytes of the constant
/// <see cref="SeedMaterial"/>, concatenated with the UTF-8 bytes of the machine name of
/// the host, hashed with SHA-256 and truncated to the 32 bytes that Ed25519 requires. The
/// resulting public key is the one that travels in the trust anchor (AD-2, AD-27).
/// </para>
/// </summary>
public sealed class Ed25519ManifestSigner : IManifestSigner
{
    /// <summary>
    /// Material of the derivation of the seed of the key of the server. It is a constant
    /// of the product and it is documented so that the derivation of the seed is
    /// reproducible (AD-2).
    /// </summary>
    public const string SeedMaterial = "lan-party-hub/server/manifest-signing-key/v1";

    /// <summary>
    /// Number of bytes that the seed of Ed25519 requires.
    /// </summary>
    public const int SeedLength = 32;

    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly string _keyId;

    /// <summary>
    /// Creates the signer of the installation manifest of the server.
    /// </summary>
    /// <param name="machineName">
    /// Name of the host whose seed is derived, or <c>null</c> to use the name of the machine
    /// that runs the process.
    /// </param>
    public Ed25519ManifestSigner(string? machineName = null)
    {
        var seed = DeriveSeed(machineName ?? Environment.MachineName);

        _privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        _keyId = ComputeKeyId(_privateKey.GeneratePublicKey().GetEncoded());
    }

    /// <summary>
    /// Derives the seed of the key of the server from the constant of the product and the
    /// name of the host. The derivation is deterministic, so the public key can be
    /// reproduced (AD-2).
    /// </summary>
    /// <param name="machineName">Name of the host whose seed is derived.</param>
    /// <returns>The 32 bytes of the seed.</returns>
    public static byte[] DeriveSeed(string machineName)
    {
        ArgumentNullException.ThrowIfNull(machineName);

        var material = Encoding.UTF8.GetBytes(SeedMaterial);
        var host = Encoding.UTF8.GetBytes(machineName);
        var buffer = new byte[material.Length + host.Length];

        material.CopyTo(buffer, 0);
        host.CopyTo(buffer, material.Length);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the public key of the server for a given host, so that a verifier can
    /// reproduce the key that signed a manifest (AD-2).
    /// </summary>
    /// <param name="machineName">Name of the host whose key is computed.</param>
    /// <returns>The 32 bytes of the raw public key.</returns>
    public static byte[] ComputePublicKey(string machineName)
    {
        var seed = DeriveSeed(machineName);

        return new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded();
    }

    /// <summary>
    /// Verifies a signature over a canonical manifest against the public key of a host
    /// (AD-2, AD-8).
    /// </summary>
    /// <param name="canonicalManifest">Canonical JSON text of the manifest that was signed.</param>
    /// <param name="signature">Signature over the canonical text, in lowercase hexadecimal.</param>
    /// <param name="machineName">Name of the host whose key verifies the signature.</param>
    /// <returns><c>true</c> when the signature verifies against the key of the host.</returns>
    public static bool Verify(string canonicalManifest, string signature, string machineName)
    {
        ArgumentNullException.ThrowIfNull(canonicalManifest);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(machineName);

        var raw = Convert.FromHexString(signature);

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(ComputePublicKey(machineName)));

        var bytes = Encoding.UTF8.GetBytes(canonicalManifest);
        verifier.BlockUpdate(bytes, 0, bytes.Length);

        return verifier.VerifySignature(raw);
    }

    /// <inheritdoc />
    public string Sign(string canonicalManifest)
    {
        ArgumentNullException.ThrowIfNull(canonicalManifest);

        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);

        var bytes = Encoding.UTF8.GetBytes(canonicalManifest);
        signer.BlockUpdate(bytes, 0, bytes.Length);

        return Convert.ToHexString(signer.GenerateSignature()).ToLowerInvariant();
    }

    /// <inheritdoc />
    public string GetKeyId() => _keyId;

    /// <summary>
    /// Computes the identifier of a key from its raw public key: the fingerprint of the
    /// key in hexadecimal, which is the value that the organizer contrasts out of band
    /// (AD-2, AD-27).
    /// </summary>
    /// <param name="publicKey">Raw bytes of the public key.</param>
    /// <returns>The identifier of the key.</returns>
    private static string ComputeKeyId(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        return Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
    }
}
