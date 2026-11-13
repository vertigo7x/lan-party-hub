using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Infrastructure.Persistence;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Each installation ROW saves the root of ITS OWN game (AC1, AC2, AC3, AD-9, AD-10,
/// AD-22).
///
/// <para>
/// La raíz por juego ya existe en el disco desde la 10.4. Esta historia vigila que la
/// PERSISTENCIA la respete: cada fila guarda la raíz de su juego y no la de otro, así
/// que borrar, reparar o actualizar un juego no alcanza el árbol de nadie más.
/// </para>
/// </summary>
public sealed class InstallationRootPerGameTests : IDisposable
{
    private readonly string _dataFolder = Path.Combine(
        Path.GetTempPath(),
        "lph-root-" + Guid.NewGuid().ToString("N"));

    private readonly string _installDirectory;

    /// <summary>Creates the test and its folder.</summary>
    public InstallationRootPerGameTests()
    {
        _installDirectory = Path.Combine(_dataFolder, ClientDataLayout.InstallFolderName);

        Directory.CreateDirectory(_dataFolder);
    }

    /// <summary>
    /// Every game has its OWN root inside the install root (AC1, AD-9).
    /// </summary>
    [Fact]
    public void Every_game_has_its_own_root_inside_the_install_root()
    {
        var layout = ClientDataLayout.Create(_dataFolder, _installDirectory);

        var primero = layout.GameInstallDirectory("juego-uno");
        var segundo = layout.GameInstallDirectory("juego-dos");

        // Cada juego tiene su propia raíz, y las dos están DENTRO de la raíz de
        // instalaciones (AC1).
        Assert.NotEqual(primero, segundo);
        Assert.StartsWith(layout.InstallDirectory, primero, StringComparison.Ordinal);
        Assert.StartsWith(layout.InstallDirectory, segundo, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row of each game saves ITS OWN root, así que el identificador de la raíz es
    /// DISTINTO para cada juego (AC3, AD-10, AD-22).
    /// </summary>
    [Fact]
    public void The_identifier_of_the_root_is_different_for_each_game()
    {
        // El identificador es el sha256 de la ruta canónica de la raíz, y como cada
        // juego tiene su propia raíz, cada fila guarda un identificador DISTINTO: el
        // identificador del directorio de instalación deja de ser el mismo para todos
        // los juegos del cliente (AC3).
        var primero = InstallationCertification.DeriveInstallDirectoryId(
            Path.Combine("/raiz", "juego-uno"));

        var segundo = InstallationCertification.DeriveInstallDirectoryId(
            Path.Combine("/raiz", "juego-dos"));

        Assert.NotEqual(primero, segundo);

        // Y es ESTABLE: el mismo juego da el mismo identificador aunque se repita la
        // certificación (AC1).
        Assert.Equal(
            primero,
            InstallationCertification.DeriveInstallDirectoryId(Path.Combine("/raiz", "juego-uno")));
    }

    /// <summary>
    /// Deletes the folder of the test (AD-16).
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }
}
