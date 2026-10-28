using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Layout of the data folder of the client (AD-9, AD-17).
///
/// <para>
/// The data folder of the client is <c>%LOCALAPPDATA%\LanPartyHub</c> and it holds exactly
/// four entries: the installation root, the folder of the partial downloads, the cache of the
/// signed manifests and the SQLite file of the client. The installation root is the only
/// directory that the rule of extraction confines; the folder of the partial downloads has a
/// fixed path and it lives outside that root, so the progress survives a restart (AD-9,
/// AD-11).
/// </para>
/// <para>
/// The root that lives in the data folder is the default one and it always exists, but the
/// root that confines the installation is the effective one, which the configuration can move
/// somewhere else. The default root is then left unused.
/// </para>
/// <para>
/// The folder lives outside the binary, so it survives the replacement of the binary. The
/// default is the one of AD-17 and it can be injected, because the integration level needs a
/// temporary folder.
/// </para>
/// </summary>
public sealed class ClientDataLayout : IClientDataLayout
{
    /// <summary>Name of the data folder of the client, under the local application data.</summary>
    public const string FolderName = "LanPartyHub";

    /// <summary>Name of the installation root, inside the data folder.</summary>
    public const string InstallFolderName = "install";

    /// <summary>Name of the folder of the partial downloads, inside the data folder.</summary>
    public const string DownloadsFolderName = "downloads";

    /// <summary>Name of the cache of the signed manifests, inside the data folder.</summary>
    public const string ManifestsFolderName = "manifests";

    /// <summary>Name of the SQLite file of the client, inside the data folder.</summary>
    public const string StateDatabaseFileName = "state.sqlite";

    /// <summary>Extension of the partial file of a package.</summary>
    public const string PartialFileExtension = ".part";

    private ClientDataLayout(
        string folder,
        string installDirectory,
        string defaultInstallDirectory,
        string downloadsDirectory,
        string manifestsDirectory,
        string stateDatabasePath)
    {
        Folder = folder;
        InstallDirectory = installDirectory;
        DefaultInstallDirectory = defaultInstallDirectory;
        DownloadsDirectory = downloadsDirectory;
        ManifestsDirectory = manifestsDirectory;
        StateDatabasePath = stateDatabasePath;
    }

    /// <summary>
    /// The data folder of AD-17: <c>%LOCALAPPDATA%\LanPartyHub</c>.
    /// </summary>
    public static string DefaultDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>Absolute path of the data folder of the client.</summary>
    public string Folder { get; }

    /// <summary>Absolute path of the installation root, the only confined directory.</summary>
    public string InstallDirectory { get; private set; }

    /// <summary>
    /// Absolute path of the default installation root, inside the data folder. It always
    /// exists, and it is left unused when the configuration points to another root.
    /// </summary>
    public string DefaultInstallDirectory { get; }

    /// <summary>Absolute path of the folder that holds the partial downloads.</summary>
    public string DownloadsDirectory { get; }

    /// <summary>Absolute path of the cache of the signed manifests.</summary>
    public string ManifestsDirectory { get; }

    /// <summary>Absolute path of the SQLite file of the client.</summary>
    public string StateDatabasePath { get; }

    /// <summary>
    /// Resolves the layout of the data folder. The folders are not created here: the creation
    /// is a step of the startup, so a layout can be resolved without touching the disk.
    /// </summary>
    /// <param name="dataFolder">Absolute or relative path of the data folder.</param>
    /// <param name="installDirectory">Absolute or relative path of the installation root.</param>
    /// <returns>The resolved layout.</returns>
    public static ClientDataLayout Create(string dataFolder, string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var folder = Path.GetFullPath(dataFolder);

        return new ClientDataLayout(
            folder,
            Path.GetFullPath(installDirectory),
            Path.Combine(folder, InstallFolderName),
            Path.Combine(folder, DownloadsFolderName),
            Path.Combine(folder, ManifestsFolderName),
            Path.Combine(folder, StateDatabaseFileName));
    }

    /// <inheritdoc />
    public void EnsureCreated()
    {
        CreateDirectory(Folder);
        CreateDirectory(DownloadsDirectory);
        CreateDirectory(ManifestsDirectory);

        // The default root always exists in the data folder: the layout of AD-9 holds
        // `install/`, `downloads/`, `manifests/` and `state.sqlite` whatever the
        // configuration says. It is left unused when the configuration points to another
        // root.
        CreateDirectory(DefaultInstallDirectory);

        // The root that confines the installation is the effective one.
        CreateDirectory(InstallDirectory);
    }

    /// <inheritdoc />
    public void SetInstallDirectory(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        // The root is the only directory that the rule of extraction confines, so the change
        // is applied at once and the new root becomes the only confined one (AD-9).
        InstallDirectory = Path.GetFullPath(installDirectory);

        CreateDirectory(InstallDirectory);
    }

    /// <summary>
    /// Creates a folder of the layout. The failure names the folder that could not be
    /// created, so the player knows which path has to be fixed.
    /// </summary>
    private static void CreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                $"No se puede escribir en la carpeta '{path}': {exception.Message} "
                + "Elija una carpeta con permisos de escritura o corrija los permisos de la actual.",
                exception);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"No se puede escribir en la carpeta '{path}': {exception.Message} "
                + "Compruebe que la carpeta existe y que hay espacio libre en el disco.",
                exception);
        }
    }

    /// <inheritdoc />
    public string PartialFilePath(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // The identifier of a package is an opaque string and it is used as a file name, so it
        // cannot carry a separator. The partial file lives only in the folder of the downloads
        // (AD-9) and this check is what keeps it there.
        if (packageId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || packageId.Contains('/', StringComparison.Ordinal)
            || packageId.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "El identificador de un paquete no puede usarse como nombre de archivo.",
                nameof(packageId));
        }

        var path = Path.GetFullPath(Path.Combine(DownloadsDirectory, packageId + PartialFileExtension));

        if (!IsInside(DownloadsDirectory, path))
        {
            throw new InvalidOperationException(
                $"El archivo parcial de un paquete tiene que vivir en '{DownloadsDirectory}'.");
        }

        return path;
    }

    /// <summary>
    /// Tells whether a path is inside a folder. Both paths are compared once they are fully
    /// resolved, so a relative path cannot escape the folder.
    /// </summary>
    private static bool IsInside(string folder, string path)
    {
        var root = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
