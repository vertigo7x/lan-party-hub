using LANPartyHub.Server.Infrastructure.Persistence;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The reading of the bytes of the published packages (AD-9, AD-22).
///
/// <para>
/// The identifier of the package is the name of a file inside the repository of packages
/// and it comes from the body of the request, so an identifier that is not a plain file
/// name must never make the server read a file that is outside of that repository: a
/// relative path with ".." would climb out of it, and a rooted path would make the
/// combination of paths return that absolute path (AD-9).
/// </para>
/// </summary>
public sealed class PackageContentStoreTests
{
    [Theory]
    [InlineData("../catalog.sqlite")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("..\\catalog.sqlite")]
    [InlineData("game/../../catalog.sqlite")]
    [InlineData("game\\..\\..\\catalog.sqlite")]
    [InlineData("nested/game.bin")]
    [InlineData("nested\\game.bin")]
    public async Task An_identifier_that_is_not_a_plain_file_name_reads_nothing(string packageId)
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-content-store-").FullName;

        try
        {
            var dataDirectory = DataDirectory.Create(folder);

            // El archivo que un identificador malicioso intentaría leer está fuera del
            // repositorio de paquetes, y existe de verdad, para que la prueba observe
            // que el adaptador no lo lee.
            var secret = Path.Combine(folder, DataDirectory.CatalogDatabaseFileName);
            await File.WriteAllTextAsync(secret, "contenido privado", TestContext.Current.CancellationToken);

            var store = new PackageContentStore(dataDirectory);

            var bytes = await store.ReadBytesAsync(packageId, TestContext.Current.CancellationToken);

            Assert.Null(bytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task An_absolute_identifier_reads_nothing()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-content-store-").FullName;

        try
        {
            var dataDirectory = DataDirectory.Create(folder);

            var secret = Path.Combine(folder, DataDirectory.CatalogDatabaseFileName);
            await File.WriteAllTextAsync(secret, "contenido privado", TestContext.Current.CancellationToken);

            var store = new PackageContentStore(dataDirectory);

            var bytes = await store.ReadBytesAsync(secret, TestContext.Current.CancellationToken);

            Assert.Null(bytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_plain_file_name_reads_the_bytes_of_the_package()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-content-store-").FullName;

        try
        {
            var dataDirectory = DataDirectory.Create(folder);

            var packageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40";
            var content = new byte[] { 1, 2, 3 };

            await File.WriteAllBytesAsync(Path.Combine(dataDirectory.PackagesPath, packageId), content, TestContext.Current.CancellationToken);

            var store = new PackageContentStore(dataDirectory);

            var bytes = await store.ReadBytesAsync(packageId, TestContext.Current.CancellationToken);

            Assert.NotNull(bytes);
            Assert.Equal(content, bytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
