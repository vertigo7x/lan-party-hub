using System.Security.Cryptography;
using System.Text;
using LANPartyHub.Client.Domain.Integrity;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The calculation of the <c>sha256</c> of the bytes of a package (AD-2, AD-8, AC1).
///
/// <para>
/// El hash es la comprobación de INTEGRIDAD: verifica que los bytes son los que el
/// manifiesto firmado describe. Es una comprobación DISTINTA de la de autenticidad, y
/// ninguna sustituye a la otra (AD-2).
/// </para>
/// </summary>
public sealed class PackageIntegrityTests
{
    /// <summary>
    /// The hash of a known content is the one that SHA-256 declares (AD-2).
    /// </summary>
    [Fact]
    public void The_hash_of_a_known_content_is_the_one_that_sha256_declares()
    {
        // El vector de prueba del FIPS 180-4: la cadena "abc" tiene este hash.
        var content = Encoding.UTF8.GetBytes("abc");

        var hash = PackageIntegrity.ComputeSha256(content);

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            hash);
    }

    /// <summary>
    /// The hash of an empty content is the one that SHA-256 declares (AD-2).
    /// </summary>
    [Fact]
    public void The_hash_of_an_empty_content_is_the_one_that_sha256_declares()
    {
        var hash = PackageIntegrity.ComputeSha256([]);

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            hash);
    }

    /// <summary>
    /// The hash of a stream is the same one that the hash of its bytes declares (AD-2).
    /// </summary>
    [Fact]
    public async Task The_hash_of_a_stream_is_the_same_one_that_the_hash_of_its_bytes_declares()
    {
        var content = Encoding.UTF8.GetBytes("contenido del paquete");

        await using var stream = new MemoryStream(content);

        var fromStream = await PackageIntegrity.ComputeSha256Async(stream, TestContext.Current.CancellationToken);
        var fromBytes = PackageIntegrity.ComputeSha256(content);

        Assert.Equal(fromBytes, fromStream);
    }

    /// <summary>
    /// The hash is written in LOWERCASE hexadecimal, which is the form that the manifest
    /// signed declares (AD-2).
    /// </summary>
    [Fact]
    public void The_hash_is_written_in_lowercase_hexadecimal()
    {
        var hash = PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("contenido"));

        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Equal(PackageIntegrity.Sha256HexLength, hash.Length);
    }

    /// <summary>
    /// Two different contents have two different hashes (AD-2).
    /// </summary>
    [Fact]
    public void Two_different_contents_have_two_different_hashes()
    {
        var first = PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("contenido"));
        var second = PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("contenidp"));

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The hash of the content is the one that the framework computes, so the rule of the
    /// domain is the rule del estándar (AD-2).
    /// </summary>
    [Fact]
    public void The_hash_of_the_content_is_the_one_that_the_framework_computes()
    {
        var content = Encoding.UTF8.GetBytes("el contenido del paquete");

        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        Assert.Equal(expected, PackageIntegrity.ComputeSha256(content));
    }

    /// <summary>
    /// Two hashes that describe the SAME content match, and the comparison does not
    /// distinguish the case (AD-2).
    /// </summary>
    [Fact]
    public void Two_hashes_of_the_same_content_match()
    {
        var hash = PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("contenido"));

        Assert.True(PackageIntegrity.Matches(hash, hash));
        Assert.True(PackageIntegrity.Matches(hash, hash.ToUpperInvariant()));
        Assert.True(PackageIntegrity.Matches($"  {hash}  ", hash));
    }

    /// <summary>
    /// Two hashes of DIFFERENT content do NOT match (AD-2).
    /// </summary>
    [Fact]
    public void Two_hashes_of_different_content_do_not_match()
    {
        var first = PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("contenido"));
        var second = PackageIntegrity.ComputeSha256(Encoding.UTF8.GetBytes("otro"));

        Assert.False(PackageIntegrity.Matches(first, second));
    }

    /// <summary>
    /// An ABSENT hash never matches: un manifiesto que no declara su hash no verifica
    /// nada (AD-2).
    /// </summary>
    [Theory]
    [InlineData(null, "abc")]
    [InlineData("abc", null)]
    [InlineData("", "abc")]
    [InlineData("abc", "   ")]
    public void An_absent_hash_never_matches(string? left, string? right)
    {
        Assert.False(PackageIntegrity.Matches(left, right));
    }

    /// <summary>
    /// The hash is calculated over the bytes that are READ, not over a form of the
    /// manifest: the domain no conoce el transporte (AD-1, AD-4).
    /// </summary>
    [Fact]
    public async Task The_hash_is_calculated_over_the_bytes_that_are_read()
    {
        var content = Encoding.UTF8.GetBytes("contenido del paquete");

        await using var stream = new MemoryStream(content);

        // El stream se lee desde su posición actual, así que un stream que ya se leyó
        // calcula el hash de lo que QUEDA, no el de todo el contenido.
        var first = await PackageIntegrity.ComputeSha256Async(stream, TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageIntegrity.ComputeSha256(content),
            first);
    }
}
