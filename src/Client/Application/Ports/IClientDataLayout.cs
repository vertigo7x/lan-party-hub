namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Layout of the data folder of the client (AD-9, AD-17).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that owns
/// the file system. The layout of the data folder is fixed by AD-9: the installation root, the
/// folder of the partial downloads, the cache of the signed manifests and the SQLite file of
/// the client.
/// </para>
/// </summary>
public interface IClientDataLayout
{
    /// <summary>Absolute path of the data folder of the client.</summary>
    string Folder { get; }

    /// <summary>
    /// Absolute path of the installation root. It is the only directory that the rule of
    /// extraction confines (AD-9).
    /// </summary>
    string InstallDirectory { get; }

    /// <summary>Absolute path of the folder that holds the partial downloads.</summary>
    string DownloadsDirectory { get; }

    /// <summary>Absolute path of the cache of the signed manifests.</summary>
    string ManifestsDirectory { get; }

    /// <summary>Absolute path of the SQLite file of the client.</summary>
    string StateDatabasePath { get; }

    /// <summary>
    /// Path of the partial file of a package. The partial file lives only in the folder of the
    /// downloads, never in the installation root and never in the temporary folder (AD-9).
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <returns>Absolute path of the partial file.</returns>
    string PartialFilePath(string packageId);

    /// <summary>
    /// Creates the folders of the layout. The operation is idempotent, so a second startup
    /// over a folder that already exists changes nothing.
    /// </summary>
    void EnsureCreated();

    /// <summary>
    /// Moves the installation root. The root is the only directory that the rule of extraction
    /// confines, so the change is applied at once and the new root becomes the only confined
    /// one (AD-9).
    /// </summary>
    /// <param name="installDirectory">Absolute path of the new installation root.</param>
    void SetInstallDirectory(string installDirectory);
}
