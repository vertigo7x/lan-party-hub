namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// The data folder of the event server (AD-17).
///
/// The folder holds the SQLite file of the server and the repository of the packages. It is
/// created before the connection is opened, because SQLite fails with
/// <c>SQLITE_CANTOPEN</c> when the folder of the file does not exist. The folder lives
/// outside the binary, so it survives the replacement of the binary.
/// </summary>
public sealed class DataDirectory
{
    /// <summary>Name of the SQLite file of the server.</summary>
    public const string CatalogDatabaseFileName = "catalog.sqlite";

    /// <summary>Name of the folder that holds the published packages.</summary>
    public const string PackagesFolderName = "packages";

    private DataDirectory(string folder, string catalogDatabasePath, string packagesPath)
    {
        Folder = folder;
        CatalogDatabasePath = catalogDatabasePath;
        PackagesPath = packagesPath;
    }

    /// <summary>Absolute path of the data folder.</summary>
    public string Folder { get; }

    /// <summary>Absolute path of the SQLite file of the server.</summary>
    public string CatalogDatabasePath { get; }

    /// <summary>Absolute path of the repository of the published packages.</summary>
    public string PackagesPath { get; }

    /// <summary>
    /// Resolves the data folder and creates it, together with the repository of the packages.
    /// </summary>
    /// <param name="path">Absolute or relative path of the data folder.</param>
    /// <returns>The resolved data folder.</returns>
    public static DataDirectory Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var folder = Path.GetFullPath(path);

        Directory.CreateDirectory(folder);

        var packagesPath = Path.Combine(folder, PackagesFolderName);
        Directory.CreateDirectory(packagesPath);

        return new DataDirectory(folder, Path.Combine(folder, CatalogDatabaseFileName), packagesPath);
    }
}
