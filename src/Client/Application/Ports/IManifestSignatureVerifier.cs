namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the verification of the signature of an installation manifest (AD-2, AD-8).
///
/// <para>
/// El port existe para que el caso de uso de la validación pueda pronunciarse sobre la
/// autenticidad sin conocer la criptografía: la firma es Ed25519 y la biblioteca vive
/// detrás de este port, en la capa de adapters (AD-1, AD-2).
/// </para>
/// <para>
/// El port NO invalida el ancla: un fallo de autenticidad es un manifiesto INVÁLIDO, no un
/// ancla sustituida, y el ancla fijada es inmutable (AD-27).
/// </para>
/// </summary>
public interface IManifestSignatureVerifier
{
    /// <summary>
    /// Verifies the signature of a canonical manifest against the public key of the trust
    /// anchor that is already fixed (AD-2, AD-8, AD-27).
    ///
    /// <para>
    /// Los bytes que se verifican son EXACTAMENTE los que llegan: el texto canónico tal y
    /// como el servidor lo firmó, sin recomponerlo aquí (AD-2).
    /// </para>
    /// </summary>
    /// <param name="canonicalManifest">Canonical JSON text of the manifest that was signed.</param>
    /// <param name="signature">Signature over the canonical text, in lowercase hexadecimal.</param>
    /// <param name="publicKey">Raw public key of the fixed trust anchor.</param>
    /// <returns><c>true</c> when the signature verifies against the key.</returns>
    bool Verify(string canonicalManifest, string signature, byte[] publicKey);
}
