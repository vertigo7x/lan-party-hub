using LANPartyHub.Client.Domain.Install;

namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Layout of the data folder of the client (AD-9, AD-17, FR-8).
///
/// <para>
/// The port is declared in the Application layer and it is implemented by the adapter that owns
/// the file system. The layout of the data folder is fixed by AD-9: the installation root, the
/// folder of the partial downloads, the cache of the signed manifests and the SQLite file of
/// the client.
/// </para>
/// <para>
/// La raíz de instalación es POR JUEGO: la base agrupa los juegos y cada juego tiene su
/// propia raíz, así que un juego no puede leer, escribir, sustituir ni borrar el árbol de
/// otro (AD-9, FR-8).
/// </para>
/// </summary>
public interface IClientDataLayout
{
    /// <summary>Absolute path of the data folder of the client.</summary>
    string Folder { get; }

    /// <summary>
    /// Absolute path of the BASE install directory, the folder that holds one installation
    /// root per game (AD-9, FR-8).
    ///
    /// <para>
    /// La base NO confina ninguna escritura por sí misma: la regla de extracción confina
    /// contra la raíz del JUEGO, que es su carpeta hija (AD-9, FR-8).
    /// </para>
    /// </summary>
    string InstallDirectory { get; }

    /// <summary>
    /// Absolute path of the installation root of ONE game, the folder that confines the
    /// tree of that game (AD-9, FR-8).
    ///
    /// <para>
    /// La raíz del juego es una CARPETA HIJA de la base, con el identificador del juego
    /// como nombre: dos juegos nunca comparten raíz, así que la sustitución por cambio de
    /// nombre de uno no toca el árbol del otro (AD-9, FR-8).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns>Absolute path of the installation root of that game.</returns>
    /// <exception cref="ArgumentException">
    /// The identifier cannot be used as the name of the folder of a game.
    /// </exception>
    string GameInstallDirectory(string gameId) =>
        InstallPathConfine.GameRoot(InstallDirectory, gameId);

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
    /// Moves the base install directory. The roots of the games are DERIVED from it, así que
    /// el cambio se aplica a la vez y la base nueva es la que confines las escrituras
    /// (AD-9).
    /// </summary>
    /// <param name="installDirectory">Absolute path of the new base install directory.</param>
    void SetInstallDirectory(string installDirectory);
}
