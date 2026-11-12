using LANPartyHub.Client.Infrastructure.Filesystem;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The substitution of the installation root (AD-9, AC4, FR-11).
///
/// <para>
/// La raíz de PREPARACIÓN es una HERMANA de la raíz activa: viven en el MISMO volumen,
/// así que la sustitución es un cambio de nombre, que en el mismo volumen es una
/// operación ATÓMICA (AD-9).
/// </para>
/// <para>
/// La raíz activa NO se borra hasta que la preparada ocupa su sitio: el cambio de
/// nombre de la activa a un lado y el de la preparada a su sitio son las dos
/// operaciones, y si la segunda falla, la primera se DESHACE, así que la instalación
/// anterior sigue vigente (AC4, FR-11).
/// </para>
/// <para>
/// Este nivel se ejercita contra el SISTEMA DE FICHEROS REAL, sin LAN y sin servidor
/// (AD-16).
/// </para>
/// </summary>
public sealed class InstallRootSwitcherTests : IDisposable
{
    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// The PREPARATION root is a SIBLING of the active one, never a folder inside it
    /// (AD-9): a folder inside cannot be exchanged with its mother.
    /// </summary>
    [Fact]
    public void The_preparation_root_is_a_sibling_of_the_active_one()
    {
        var active = CreateTemporaryFolder();
        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);

        var staging = switcher.PrepareStagingRoot(active);

        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(active)!, Path.GetFileName(active) + InstallRootSwitcher.StagingSuffix),
            staging);
        Assert.True(Directory.Exists(staging));
        Assert.False(
            staging.StartsWith(active + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "la raíz de preparación no puede estar DENTRO de la activa: no se podrían intercambiar");
    }

    /// <summary>
    /// The PREPARATION root starts EMPTY: the new tree cannot inherit a single file of
    /// the previous version (AC4).
    /// </summary>
    [Fact]
    public void The_preparation_root_starts_empty()
    {
        var active = CreateTemporaryFolder();
        Directory.CreateDirectory(Path.Combine(active, "juego"));
        File.WriteAllText(Path.Combine(active, "juego", "datos.bin"), "contenido anterior");

        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);
        var staging = switcher.PrepareStagingRoot(active);

        Assert.Empty(Directory.GetFileSystemEntries(staging));
    }

    /// <summary>
    /// The PREPARATION does not touch the active root: the previous version stays intact
    /// and playable until the substitution (AC4, FR-11).
    /// </summary>
    [Fact]
    public void The_preparation_does_not_touch_the_active_root()
    {
        var active = CreateTemporaryFolder();
        Directory.CreateDirectory(Path.Combine(active, "juego"));
        File.WriteAllText(Path.Combine(active, "juego", "datos.bin"), "contenido anterior");

        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);
        switcher.PrepareStagingRoot(active);

        Assert.True(File.Exists(Path.Combine(active, "juego", "datos.bin")));
    }

    /// <summary>
    /// The SUBSTITUTION publishes the prepared tree in the site of the active one, and the
    /// previous version is discarded (AC1).
    /// </summary>
    [Fact]
    public void The_substitution_publishes_the_prepared_tree_in_the_active_root()
    {
        var active = CreateTemporaryFolder();
        Directory.CreateDirectory(Path.Combine(active, "juego"));
        File.WriteAllText(Path.Combine(active, "juego", "datos.bin"), "contenido anterior");

        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);
        var staging = switcher.PrepareStagingRoot(active);
        Directory.CreateDirectory(Path.Combine(staging, "juego"));
        File.WriteAllText(Path.Combine(staging, "juego", "datos.bin"), "contenido nuevo");

        switcher.CommitStagingRoot(active, staging);

        Assert.True(File.Exists(Path.Combine(active, "juego", "datos.bin")));
        Assert.Equal("contenido nuevo", File.ReadAllText(Path.Combine(active, "juego", "datos.bin")));
        Assert.False(Directory.Exists(staging));
    }

    /// <summary>
    /// The SUBSTITUTION does not leave the replaced root behind: the version that is
    /// discarded is the one that was there, not a copy of it (AC1).
    /// </summary>
    [Fact]
    public void The_substitution_does_not_leave_the_replaced_root_behind()
    {
        var active = CreateTemporaryFolder();
        Directory.CreateDirectory(Path.Combine(active, "juego"));

        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);
        var staging = switcher.PrepareStagingRoot(active);
        switcher.CommitStagingRoot(active, staging);

        var replaced = Path.Combine(
            Path.GetDirectoryName(active)!,
            Path.GetFileName(active) + InstallRootSwitcher.ReplacedSuffix);

        Assert.False(Directory.Exists(replaced));
    }

    /// <summary>
    /// The DISCARD of the preparation NEVER touches the active root (AC4, FR-11): a
    /// failure of the new version cannot invalidate the installation that is already
    /// valid.
    /// </summary>
    [Fact]
    public void The_discard_of_the_preparation_never_touches_the_active_root()
    {
        var active = CreateTemporaryFolder();
        Directory.CreateDirectory(Path.Combine(active, "juego"));
        File.WriteAllText(Path.Combine(active, "juego", "datos.bin"), "contenido anterior");

        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);
        var staging = switcher.PrepareStagingRoot(active);
        Directory.CreateDirectory(Path.Combine(staging, "juego"));

        switcher.DiscardStagingRoot(active, staging);

        Assert.False(Directory.Exists(staging));
        Assert.True(File.Exists(Path.Combine(active, "juego", "datos.bin")));
        Assert.Equal("contenido anterior", File.ReadAllText(Path.Combine(active, "juego", "datos.bin")));
    }

    /// <summary>
    /// A SUBSTITUTION whose publication fails leaves the previous version in its place
    /// (AC4, FR-11): a player cannot be left without the game.
    /// </summary>
    [Fact]
    public void A_failed_publication_leaves_the_previous_version_in_its_place()
    {
        var active = CreateTemporaryFolder();
        Directory.CreateDirectory(Path.Combine(active, "juego"));
        File.WriteAllText(Path.Combine(active, "juego", "datos.bin"), "contenido anterior");

        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);

        // La raíz de preparación NO existe: la publicación no puede completarse.
        var staging = Path.Combine(
            Path.GetDirectoryName(active)!,
            Path.GetFileName(active) + InstallRootSwitcher.StagingSuffix);

        Assert.Throws<DirectoryNotFoundException>(() => switcher.CommitStagingRoot(active, staging));

        Assert.True(File.Exists(Path.Combine(active, "juego", "datos.bin")));
        Assert.Equal("contenido anterior", File.ReadAllText(Path.Combine(active, "juego", "datos.bin")));
    }

    /// <summary>
    /// The PREPARATION root cannot be the active one: the two are different roots by
    /// definition, so the substitution has something to exchange.
    /// </summary>
    [Fact]
    public void The_preparation_root_cannot_be_the_active_one()
    {
        var active = CreateTemporaryFolder();
        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);

        Assert.Throws<InvalidOperationException>(
            () => switcher.CommitStagingRoot(active, active));
        Assert.Throws<InvalidOperationException>(
            () => switcher.DiscardStagingRoot(active, active));
    }

    /// <summary>Creates a temporary folder for the test.</summary>
    /// <returns>The absolute path of the folder.</returns>
    private string CreateTemporaryFolder()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "lan-party-hub-switcher-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        _temporaryFolders.Add(path);

        return path;
    }

    /// <summary>Removes the temporary folders of the test.</summary>
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException)
            {
                // Un resto que no se puede borrar no invalida la prueba.
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// La raíz de PREPARACIÓN es hermana de la raíz del JUEGO que se está instalando, y
    /// nunca hermana de la de otro juego: por eso la sustitución de un juego no toca el
    /// árbol de otro (FR-8, FR-11, AD-9).
    /// </summary>
    [Fact]
    public void The_preparation_root_is_a_sibling_of_the_root_of_its_game()
    {
        var folder = CreateTemporaryFolder();
        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);

        var rootOfThis = Path.Combine(folder, "juego-uno");
        var rootOfOther = Path.Combine(folder, "juego-dos");

        var staging = switcher.PrepareStagingRoot(rootOfThis);

        Assert.Equal(Path.Combine(folder, "juego-uno" + InstallRootSwitcher.StagingSuffix), staging);
        Assert.NotEqual(Path.Combine(folder, "juego-dos" + InstallRootSwitcher.StagingSuffix), staging);
    }

    /// <summary>
    /// El árbol de un juego que quedó en la raíz COMPARTIDA se lleva a la raíz de SU
    /// juego con un cambio de nombre: si no se moviera, el juego parecería no
    /// instalado (FR-8, AD-9).
    /// </summary>
    [Fact]
    public void The_tree_of_a_game_is_moved_to_the_root_of_that_game()
    {
        var folder = CreateTemporaryFolder();
        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);

        var shared = Path.Combine(folder, "compartida");
        var target = Path.Combine(folder, "juego-uno");

        Directory.CreateDirectory(Path.Combine(shared, "game"));
        File.WriteAllText(Path.Combine(shared, "game", "datos.bin"), "contenido");

        Assert.True(switcher.MoveRoot(shared, target));

        Assert.False(Directory.Exists(shared));
        Assert.True(File.Exists(Path.Combine(target, "game", "datos.bin")));
    }

    /// <summary>
    /// Un movimiento que no tiene sentido NO se fuerza: si el árbol ya está en su
    /// sitio, o si no hay árbol que llevar, no se mueve nada (AD-9).
    /// </summary>
    [Fact]
    public void A_move_without_a_tree_or_to_the_same_place_does_nothing()
    {
        var folder = CreateTemporaryFolder();
        var switcher = new InstallRootSwitcher(NullLogger<InstallRootSwitcher>.Instance);

        var target = Path.Combine(folder, "juego-uno");

        Directory.CreateDirectory(target);

        // No hay arbol en el sitio viejo.
        Assert.False(switcher.MoveRoot(Path.Combine(folder, "no-existe"), target));

        // El arbol ya esta donde tiene que estar.
        Assert.False(switcher.MoveRoot(target, target));
    }

}
