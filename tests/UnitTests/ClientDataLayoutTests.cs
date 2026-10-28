using LANPartyHub.Client.Infrastructure.Persistence;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Layout of the data folder of the client (AD-9, AD-17).
///
/// <para>
/// The partial file of a package lives only in the folder of the downloads, and the identifier
/// of the package is used as its name, so an identifier that carries a separator has to be
/// rejected instead of writing outside that folder.
/// </para>
/// </summary>
public sealed class ClientDataLayoutTests : IDisposable
{
    private readonly string _dataFolder;
    private readonly string _installDirectory;

    public ClientDataLayoutTests()
    {
        _dataFolder = Directory.CreateTempSubdirectory("lan-party-hub-client-layout-").FullName;
        _installDirectory = Path.Combine(_dataFolder, ClientDataLayout.InstallFolderName);
    }

    [Fact]
    public void The_partial_file_of_a_package_lives_in_the_folder_of_the_downloads()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        var path = layout.PartialFilePath("paquete-1");

        Assert.Equal(
            Path.Combine(_dataFolder, ClientDataLayout.DownloadsFolderName, "paquete-1.part"),
            path);

        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(_dataFolder, ClientDataLayout.DownloadsFolderName)),
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("paquete\\con\\separador")]
    [InlineData("paquete/con/separador")]
    [InlineData("paquete:con\\caracter")]
    [InlineData("paquete*")]
    public void A_package_id_that_cannot_be_a_file_name_is_rejected(string packageId)
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        Assert.Throws<ArgumentException>(() => layout.PartialFilePath(packageId));
    }

    [Fact]
    public void The_layout_creates_the_four_entries_of_the_data_folder()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        layout.EnsureCreated();

        Assert.True(Directory.Exists(Path.Combine(_dataFolder, ClientDataLayout.InstallFolderName)));
        Assert.True(Directory.Exists(Path.Combine(_dataFolder, ClientDataLayout.DownloadsFolderName)));
        Assert.True(Directory.Exists(Path.Combine(_dataFolder, ClientDataLayout.ManifestsFolderName)));
    }

    [Fact]
    public void The_default_root_exists_even_when_the_configuration_points_to_another_one()
    {
        var otherRoot = Path.Combine(_dataFolder, "juegos");

        var layout = ClientDataLayout.Create(_dataFolder, otherRoot);

        layout.EnsureCreated();

        Assert.True(Directory.Exists(Path.Combine(_dataFolder, ClientDataLayout.InstallFolderName)));
        Assert.True(Directory.Exists(otherRoot));
        Assert.Equal(Path.GetFullPath(otherRoot), layout.InstallDirectory);
    }

    [Fact]
    public void The_installation_root_moves_and_the_new_root_is_created()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        var newRoot = Path.Combine(_dataFolder, "juegos");

        layout.SetInstallDirectory(newRoot);

        Assert.Equal(Path.GetFullPath(newRoot), layout.InstallDirectory);
        Assert.True(Directory.Exists(newRoot));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
        catch (IOException)
        {
            // A file that the operating system still holds does not fail the test.
        }
    }
}
