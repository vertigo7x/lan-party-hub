using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Filesystem;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The resolution of the paths of the installation (AD-9, AC5).
///
/// <para>
/// La resoluciÃ³n canÃ³nica y el confine se ejercitan contra el SISTEMA DE FICHEROS REAL,
/// sin LAN y sin servidor (AD-16).
/// </para>
/// <para>
/// Este nivel es la BASE de los confines de la extracciÃ³n de la historia 7.2: aquÃ­ se
/// comprueba que la ruta canÃ³nica resuelta queda DENTRO de la raÃ­z configurada (AD-9).
/// </para>
/// </summary>
public sealed class InstallPathResolverTests : IDisposable
{
    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A relative path of the manifest resolves INSIDE the configured root (AD-9).
    /// </summary>
    [Fact]
    public void A_relative_path_resolves_inside_the_root()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        var resolved = resolver.ResolveInsideRoot("game/data.bin");

        Assert.StartsWith(root, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(resolver.IsInsideRoot("game/data.bin"));
    }

    /// <summary>
    /// The root itself is a LEGAL destination: a path that resolves to the root stays
    /// inside it (AD-9).
    /// </summary>
    [Fact]
    public void The_root_itself_is_a_legal_destination()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        Assert.True(resolver.IsInsideRoot("."));
        Assert.True(resolver.IsInsideRoot(string.Empty) is false);
    }

    /// <summary>
    /// A path that TRAVERSES out of the root with <c>..</c> is REJECTED (AD-9).
    /// </summary>
    [Fact]
    public void A_path_that_traverses_out_of_the_root_is_rejected()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        Assert.False(resolver.IsInsideRoot("../fuera.txt"));
        Assert.False(resolver.IsInsideRoot("game/../../fuera.txt"));

        var exception = Assert.Throws<InstallPathOutsideRootException>(
            () => resolver.ResolveInsideRoot("../fuera.txt"));

        // El rechazo nombra la ruta CANÃ“NICA que lo intentÃ³ (AD-9).
        Assert.Equal("../fuera.txt", exception.RelativePath);
        Assert.Equal(root, exception.RootPath);
        Assert.False(exception.CanonicalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An ABSOLUTE path of the manifest is NOT admitted: it resolves outside the root
    /// and the operation is rejected with its cause (AD-9, AC2).
    /// </summary>
    [Fact]
    public void An_absolute_path_of_the_manifest_is_rejected()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        var absolute = Path.Combine(Path.GetTempPath(), "fuera-de-la-raiz.txt");

        Assert.False(resolver.IsInsideRoot(absolute));
        Assert.Throws<InstallPathOutsideRootException>(() => resolver.ResolveInsideRoot(absolute));
    }

    /// <summary>
    /// An absolute path that happens to point INSIDE the root is admitted, because the
    /// confinement is about the DESTINATION and no about the FORM of the path (AD-9).
    /// </summary>
    [Fact]
    public void An_absolute_path_inside_the_root_is_admitted()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        var inside = Path.Combine(root, "game", "data.bin");

        Assert.True(resolver.IsInsideRoot(inside));
    }

    /// <summary>
    /// A SIBLING directory whose name starts with the name of the root is NOT inside it:
    /// the confine compara la ruta COMPLETA y no un prefijo de texto (AD-9).
    /// </summary>
    [Fact]
    public void A_sibling_directory_that_shares_the_prefix_of_the_root_is_not_inside_it()
    {
        var folder = CreateTemporaryFolder();

        var root = Path.Combine(folder, "install");
        var sibling = Path.Combine(folder, "install-otro");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);

        var resolver = new InstallPathResolver(root);

        Assert.False(resolver.IsInsideRoot(Path.Combine(sibling, "datos.txt")));
    }

    /// <summary>
    /// The resolution is CANONICAL: the same relative path resolves to the same absolute
    /// path no matter how it is written (AD-9).
    /// </summary>
    [Fact]
    public void The_resolution_is_canonical()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        var first = resolver.ResolveInsideRoot("game/data.bin");
        var second = resolver.ResolveInsideRoot("./game/sub/../data.bin");

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A path that escapes the root through a SYMBOLIC LINK is rejected: la comprobaciÃ³n
    /// se hace sobre la ruta canÃ³nica, tras resolver los enlaces simbÃ³licos (AD-9).
    /// </summary>
    [Fact]
    public void A_path_that_escapes_the_root_through_a_symbolic_link_is_rejected()
    {
        var folder = CreateTemporaryFolder();

        var root = Path.Combine(folder, "install");
        var outside = Path.Combine(folder, "fuera");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var link = Path.Combine(root, "enlace");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // La mÃ¡quina de la prueba no puede crear enlaces simbÃ³licos: la comprobaciÃ³n
            // del confine sobre la ruta canÃ³nica no se puede ejercitar aquÃ­.
            return;
        }

        var resolver = new InstallPathResolver(root);

        Assert.False(resolver.IsInsideRoot(Path.Combine("enlace", "datos.txt")));
    }

    /// <summary>
    /// The resolver REJECTS an empty path: the manifest cannot declare a destination
    /// that does not exist (AD-9).
    /// </summary>
    [Fact]
    public void The_resolver_rejects_an_empty_path()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        Assert.Throws<ArgumentException>(() => resolver.ResolveInsideRoot("   "));
        Assert.False(resolver.IsInsideRoot("   "));
    }

    /// <summary>
    /// The resolver REJECTS a root that is empty (AD-9).
    /// </summary>
    [Fact]
    public void The_resolver_rejects_an_empty_root()
    {
        Assert.Throws<ArgumentException>(() => new InstallPathResolver("  "));
    }

    /// <summary>
    /// The root of the resolver is the CANONICAL path of the configured root (AD-9).
    /// </summary>
    [Fact]
    public void The_root_of_the_resolver_is_the_canonical_path()
    {
        var root = CreateTemporaryFolder();
        var resolver = new InstallPathResolver(root);

        Assert.Equal(Path.GetFullPath(root), resolver.RootPath);
    }

    /// <summary>
    /// Creates a temporary folder for the test.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-install-paths-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file that the operating system still holds does not fail the test.
            }
            catch (UnauthorizedAccessException)
            {
                // A file that the operating system still holds does not fail the test.
            }
        }
    }
}
