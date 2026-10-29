namespace LANPartyHub.Server.Application.Ports;

/// <summary>
/// Port of the signature of the installation manifest (AD-2, AD-8).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that
/// holds the private key of the server. The use case that publishes a package depends on
/// this port and never on the cryptography: it hands the canonical text of the manifest and
/// receives the signature, and it never learns which algorithm produced it (AD-1).
/// </para>
/// <para>
/// The signature covers the <em>canonical form</em> of the manifest and not an arbitrary
/// serialization of it, so the text that this port receives is the same text that is
/// persisted in the column <c>canonicalManifest</c> and the same text that a verifier
/// recomputes (AD-2).
/// </para>
/// </summary>
public interface IManifestSigner
{
    /// <summary>
    /// Signs the canonical text of an installation manifest.
    /// </summary>
    /// <param name="canonicalManifest">Canonical JSON text of the manifest to sign.</param>
    /// <returns>The signature, in lowercase hexadecimal.</returns>
    string Sign(string canonicalManifest);

    /// <summary>
    /// Identifier of the key that produces the signature. It travels only in the signed
    /// envelope of the manifest and never in the descriptor of the package (AD-2, AD-7).
    /// </summary>
    /// <returns>The identifier of the signing key.</returns>
    string GetKeyId();
}
