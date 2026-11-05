using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Persistence;
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
        var resolver = CreateResolver(root);

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
        var resolver = CreateResolver(root);

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
        var resolver = CreateResolver(root);

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
        var resolver = CreateResolver(root);

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
        var resolver = CreateResolver(root);

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

        var resolver = CreateResolver(root);

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
        var resolver = CreateResolver(root);

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

        if (!TryCreateSymbolicLink(link, outside))
        {
            // La máquina de la prueba no puede crear enlaces simbólicos: la comprobación
            // del confine sobre la ruta canónica no se puede ejercitar aquí.
            return;
        }

        var resolver = CreateResolver(root);

        Assert.False(resolver.IsInsideRoot(Path.Combine("enlace", "datos.txt")));
    }

    /// <summary>
    /// A path that escapes the root through an INTERMEDIATE link is rejected: the last
    /// segment of a write path does not exist yet, so the link that has to be resolved is
    /// the one of a middle DIRECTORY (AD-9).
    ///
    /// <para>
    /// La ruta sale de la raíz por un enlace INTERMEDIO y el último segmento no existe
    /// todavía: resolver sólo el último segmento dejaría pasar la escapada.
    /// </para>
    /// </summary>
    [Fact]
    public void A_path_that_escapes_the_root_through_an_intermediate_link_is_rejected()
    {
        var folder = CreateTemporaryFolder();

        var root = Path.Combine(folder, "install");
        var outside = Path.Combine(folder, "fuera");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var link = Path.Combine(root, "enlace");

        if (!TryCreateDirectoryLink(link, outside))
        {
            // La máquina de la prueba no puede crear enlaces de directorio: la
            // comprobación del confine sobre la ruta canónica no se puede ejercitar aquí.
            return;
        }

        var resolver = CreateResolver(root);

        // El archivo final NO existe: el enlace que se atraviesa es el intermedio.
        Assert.False(resolver.IsInsideRoot(Path.Combine("enlace", "datos.txt")));

        var exception = Assert.Throws<InstallPathOutsideRootException>(
            () => resolver.ResolveInsideRoot(Path.Combine("enlace", "datos.txt")));

        // El rechazo nombra la ruta CANÓNICA que lo intentó: la del destino real del
        // enlace, que es la que sale de la raíz (AD-9).
        Assert.Equal(
            Path.GetFullPath(Path.Combine(outside, "datos.txt")),
            exception.CanonicalPath);
    }

    /// <summary>
    /// A path that escapes the root through a CHAIN of intermediate links is rejected: the
    /// canonical path resolves the links until their FINAL target (AD-9).
    /// </summary>
    [Fact]
    public void A_path_that_escapes_the_root_through_a_chain_of_links_is_rejected()
    {
        var folder = CreateTemporaryFolder();

        var root = Path.Combine(folder, "install");
        var outside = Path.Combine(folder, "fuera");
        var deeper = Path.Combine(folder, "mas-fuera");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(deeper);

        if (!TryCreateDirectoryLink(Path.Combine(root, "enlace"), outside))
        {
            return;
        }

        if (!TryCreateDirectoryLink(Path.Combine(outside, "nivel2"), deeper))
        {
            return;
        }

        var resolver = CreateResolver(root);

        Assert.False(resolver.IsInsideRoot(Path.Combine("enlace", "nivel2", "datos.txt")));
    }

    /// <summary>
    /// An intermediate link that points INSIDE the root is ADMITTED: el confine se
    /// comprueba sobre el destino real, no sobre la forma de la ruta (AD-9).
    /// </summary>
    [Fact]
    public void An_intermediate_link_that_points_inside_the_root_is_admitted()
    {
        var folder = CreateTemporaryFolder();

        var root = Path.Combine(folder, "install");
        var inside = Path.Combine(root, "real");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(inside);

        if (!TryCreateDirectoryLink(Path.Combine(root, "dentro"), inside))
        {
            return;
        }

        var resolver = CreateResolver(root);

        Assert.True(resolver.IsInsideRoot(Path.Combine("dentro", "datos.txt")));
    }

    /// <summary>
    /// A root that is ITSELF a link is canonicalized to its final target, so the confine
    /// is checked against the real destination (AD-9).
    /// </summary>
    [Fact]
    public void A_root_that_is_a_link_resolves_to_its_final_target()
    {
        var folder = CreateTemporaryFolder();

        var root = Path.Combine(folder, "install");
        var link = Path.Combine(folder, "install-enlace");

        Directory.CreateDirectory(root);

        if (!TryCreateDirectoryLink(link, root))
        {
            return;
        }

        var resolver = CreateResolver(link);

        Assert.Equal(Path.GetFullPath(root), resolver.RootPath);
        Assert.True(resolver.IsInsideRoot(Path.Combine("game", "data.bin")));
    }

    /// <summary>
    /// The resolver REJECTS an empty path: the manifest cannot declare a destination
    /// that does not exist (AD-9).
    /// </summary>
    [Fact]
    public void The_resolver_rejects_an_empty_path()
    {
        var root = CreateTemporaryFolder();
        var resolver = CreateResolver(root);

        Assert.Throws<ArgumentException>(() => resolver.ResolveInsideRoot("   "));
        Assert.False(resolver.IsInsideRoot("   "));
    }

    /// <summary>
    /// The resolver REJECTS a root that is empty (AD-9).
    ///
    /// <para>
    /// El layout real rechaza el directorio vacío al crearse, así que aquí se usa un
    /// layout mínimo cuyo directorio de instalación está vacío: la guarda del
    /// resolvedor es la que se ejercita (AD-9).
    /// </para>
    /// </summary>
    [Fact]
    public void The_resolver_rejects_an_empty_root()
    {
        var layout = new EmptyRootLayout();

        var resolver = new InstallPathResolver(layout);

        Assert.Throws<ArgumentException>(() => resolver.RootPath);
        Assert.Throws<ArgumentException>(() => resolver.ResolveInsideRoot("game/data.bin"));
    }

    /// <summary>
    /// Layout whose effective installation root is empty.
    /// </summary>
    private sealed class EmptyRootLayout : IClientDataLayout
    {
        public string Folder => string.Empty;

        public string InstallDirectory => "  ";

        public string DownloadsDirectory => string.Empty;

        public string ManifestsDirectory => string.Empty;

        public string StateDatabasePath => string.Empty;

        public string PartialFilePath(string packageId) => string.Empty;

        public void EnsureCreated()
        {
        }

        public void SetInstallDirectory(string installDirectory)
        {
        }
    }

    /// <summary>
    /// The root of the resolver is the CANONICAL path of the configured root (AD-9).
    /// </summary>
    [Fact]
    public void The_root_of_the_resolver_is_the_canonical_path()
    {
        var root = CreateTemporaryFolder();
        var resolver = CreateResolver(root);

        Assert.Equal(Path.GetFullPath(root), resolver.RootPath);
    }

    /// <summary>
    /// Creates a symbolic link to a directory, when the machine allows it.
    /// </summary>
    /// <param name="link">Path of the link to create.</param>
    /// <param name="target">Path the link points to.</param>
    /// <returns><c>true</c> when the link was created.</returns>
    private static bool TryCreateSymbolicLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a DIRECTORY LINK to a directory, when the machine allows it.
    ///
    /// <para>
    /// Una unión de directorios es un enlace que el sistema de archivos resuelve sin
    /// privilegios de administrador, a diferencia del enlace simbólico: permite
    /// ejercitar el confine sobre la ruta canónica en una máquina sin elevación.
    /// </para>
    /// </summary>
    /// <param name="link">Path of the link to create.</param>
    /// <param name="target">Path the link points to.</param>
    /// <returns><c>true</c> when the link was created.</returns>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return TryCreateSymbolicLink(link, target);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a resolver over a LAYOUT whose effective root is the given path: la raíz
    /// se lee del layout en cada llamada, así que el resolvedor la toma de ahí (AD-9).
    /// </summary>
    /// <param name="root">Absolute path of the installation root.</param>
    /// <returns>The resolver over the layout.</returns>
    private static InstallPathResolver CreateResolver(string root)
    {
        var folder = Path.GetDirectoryName(root) ?? root;

        var layout = ClientDataLayout.Create(folder, root);

        return new InstallPathResolver(layout);
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
