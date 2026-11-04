using System.Text;
using LANPartyHub.Client.Application.Ports;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace LANPartyHub.Client.Infrastructure.Trust;

/// <summary>
/// Adapter of the verification of the signature of an installation manifest with Ed25519
/// (AD-2, AD-8).
///
/// <para>
/// La firma es Ed25519 sobre el texto CANÓNICO del manifiesto, y el texto que se verifica
/// es EXACTAMENTE el que llegó del servidor: no se recompone aquí, porque dos
/// canonicalizadores podrían producir dos textos distintos para un mismo manifiesto y sólo
/// uno de ellos verificaría (AD-2).
/// </para>
/// <para>
/// La biblioteca criptográfica vive detrás del port
/// <see cref="IManifestSignatureVerifier"/>, así que sus tipos no cruzan la frontera de
/// este archivo y el caso de uso nunca los conoce (AD-1).
/// </para>
/// <para>
/// El ancla fijada es INMUTABLE: un fallo de verificación NO invalida el ancla, porque un
/// manifiesto que no valida es un manifiesto INVÁLIDO y no una ancla sustituida (AD-27).
/// </para>
/// </summary>
public sealed class Ed25519ManifestVerifier : IManifestSignatureVerifier
{
    /// <summary>
    /// Length of the raw public key of Ed25519 (AD-2).
    /// </summary>
    public const int PublicKeyLength = 32;

    /// <summary>
    /// Length of the raw signature of Ed25519 (AD-2).
    /// </summary>
    public const int SignatureLength = 64;

    /// <inheritdoc />
    public bool Verify(string canonicalManifest, string signature, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(canonicalManifest);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (publicKey.Length != PublicKeyLength)
        {
            return false;
        }

        byte[] raw;

        try
        {
            raw = Convert.FromHexString(signature.Trim());
        }
        catch (FormatException)
        {
            // Una firma que no es hexadecimal no es una firma: la verificación falla, y no
            // hay excepción que llegue al caso de uso (AD-15).
            return false;
        }

        if (raw.Length != SignatureLength)
        {
            return false;
        }

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));

            var bytes = Encoding.UTF8.GetBytes(canonicalManifest);
            verifier.BlockUpdate(bytes, 0, bytes.Length);

            return verifier.VerifySignature(raw);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Una clave o una firma que la biblioteca no puede leer es una firma que no
            // valida (AD-15).
            return false;
        }
    }
}
