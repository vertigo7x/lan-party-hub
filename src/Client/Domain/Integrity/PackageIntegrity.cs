using System.Security.Cryptography;

namespace LANPartyHub.Client.Domain.Integrity;

/// <summary>
/// Integrity of the bytes of a package (AD-2, AD-8).
///
/// <para>
/// La comprobación de integridad y la de autenticidad son DOS comprobaciones DISTINTAS y
/// ninguna sustituye a la otra: el <c>sha256</c> verifica que los bytes son los que el
/// manifiesto firmado describe, y la firma verifica que el manifiesto es del servidor que
/// el jugador emparejó. Un checksum que se usara como si probara la autenticidad dejaría
/// pasar un paquete reempaquetado por un intermediario (AD-2).
/// </para>
/// <para>
/// El hash se calcula sobre los BYTES del archivo, y no sobre una forma del manifiesto que
/// este lado componga: la comparación es contra el <c>sha256</c> que declara el manifiesto
/// FIRMADO, de modo que la correspondencia entre el manifiesto y el paquete está firmada y
/// no supuesta (AD-2).
/// </para>
/// <para>
/// El tipo vive en la capa de Domain porque es una regla del dominio y no un detalle de
/// transporte: el valor se compara, se decide sobre él y se muestra, y ninguno de esos tres
/// actos depende de cómo llegaron los bytes (AD-1, AD-8).
/// </para>
/// </summary>
public static class PackageIntegrity
{
    /// <summary>
    /// Number of characters of the hexadecimal <c>sha256</c> (AD-2).
    /// </summary>
    public const int Sha256HexLength = 64;

    /// <summary>
    /// Computes the <c>sha256</c> of a stream, reading it from its current position to its
    /// end.
    /// </summary>
    /// <param name="content">Stream that holds the bytes of the package.</param>
    /// <param name="cancellationToken">Token that cancels the reading.</param>
    /// <returns>The hexadecimal <c>sha256</c> of the bytes, in lowercase.</returns>
    /// <exception cref="ArgumentNullException">The stream is <c>null</c>.</exception>
    public static async Task<string> ComputeSha256Async(
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the <c>sha256</c> of a byte array.
    /// </summary>
    /// <param name="content">Bytes of the package.</param>
    /// <returns>The hexadecimal <c>sha256</c> of the bytes, in lowercase.</returns>
    /// <exception cref="ArgumentNullException">The array is <c>null</c>.</exception>
    public static string ComputeSha256(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    /// <summary>
    /// Tells whether two hashes describe the SAME content.
    ///
    /// <para>
    /// La comparación NO distingue mayúsculas de minúsculas: el hash se escribe en
    /// minúsculas en el manifiesto firmado y en hexadecimal en minúsculas en la
    /// comparación, y una diferencia de caja no puede ser una diferencia de contenido
    /// (AD-2).
    /// </para>
    /// </summary>
    /// <param name="left">First hash.</param>
    /// <param name="right">Second hash.</param>
    /// <returns><c>true</c> when both hashes describe the same content.</returns>
    public static bool Matches(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
