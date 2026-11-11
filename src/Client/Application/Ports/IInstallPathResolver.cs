namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the resolution of the paths of the installation (AD-9, AC1, AC3, AC4).
///
/// <para>
/// El port existe para que el caso de uso de la instalación pueda pronunciarse sobre el
/// CONFINÉ de las rutas sin conocer el disco: la resolución canónica y la comprobación
/// de que la ruta queda dentro de la raíz son una regla de AD-9 y el adaptador la
/// aplica contra el sistema de archivos (AD-1, AD-9).
/// </para>
/// <para>
/// La raíz es POR JUEGO: cada juego tiene su propia raíz de instalación, así que el
/// confine se comprueba contra la raíz de ESE juego y nunca contra la carpeta
/// compartida que las agrupa (AD-9, FR-8). El port NO expone una raíz global: una
/// raíz global dejaría pasar la escritura de un juego en el árbol de otro (AD-9,
/// FR-8, R3).
/// </para>
/// <para>
/// La raíz se LEE DEL LAYOUT en cada llamada, sin caché entre escrituras: una ruta que
/// era válida deja de serlo en cuanto la raíz se mueve o en cuanto aparece un enlace,
/// así que una copia congelada de la raíz dejaría pasar la escapada (AD-9, AC1).
/// </para>
/// <para>
/// Toda ruta del manifiesto es RELATIVA a la raíz de instalación: el manifiesto no
/// admite rutas absolutas (AD-9, AC2).
/// </para>
/// </summary>
public interface IInstallPathResolver
{
    /// <summary>
    /// Canonical absolute path of the installation root of a GAME (AD-9, FR-8).
    ///
    /// <para>
    /// Es el valor DEL MOMENTO: la raíz se lee del layout en cada llamada, así que este
    /// valor refleja la configuración vigente, no una copia congelada (AD-9).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame whose root is resolved.</param>
    /// <returns>The canonical absolute path of the installation root of that game.</returns>
    /// <exception cref="ArgumentException">
    /// The identifier cannot be used as the name of the folder of a game.
    /// </exception>
    string GameRootPath(string gameId);

    /// <summary>
    /// Resolves a relative path of the manifest against the installation root of a GAME
    /// and REQUIRES that the resolved canonical path stays inside THAT root (AD-9,
    /// AC4).
    ///
    /// <para>
    /// Un destino fuera de la raíz se RECHAZA, y el rechazo nombra la ruta que lo
    /// intentó: la ruta canónica se comprueba, y no la forma que el manifiesto escribió
    /// (AD-9).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is being installed.</param>
    /// <param name="relativePath">Path of the manifest, relative to the root of that game.</param>
    /// <returns>The canonical absolute path that resolves inside that root.</returns>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    /// <exception cref="InstallPathOutsideRootException">
    /// The resolved canonical path does NOT stay inside the installation root of that
    /// game (AD-9).
    /// </exception>
    string ResolveInsideGameRoot(string gameId, string relativePath);

    /// <summary>
    /// Tells whether a relative path of the manifest resolves inside the installation
    /// root of a GAME, without throwing (AD-9, AC4, AC5).
    ///
    /// <para>
    /// El confine se evalúa contra la raíz del JUEGO: una ruta que cae en la raíz de
    /// otro juego queda FUERA, así que un juego no puede escribir en el árbol de otro
    /// aunque compartan volumen (AD-9, FR-8).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is being installed.</param>
    /// <param name="relativePath">Path of the manifest, relative to the root of that game.</param>
    /// <returns><c>true</c> when the path resolves inside that root.</returns>
    bool IsInsideGameRoot(string gameId, string relativePath);

    /// <summary>
    /// Resolves a relative path of the manifest against an EXPLICIT root and REQUIRES
    /// that the resolved canonical path stays inside THAT root (AD-9, AC4).
    ///
    /// <para>
    /// La resolución contra la raíz del JUEGO es la de
    /// <see cref="ResolveInsideGameRoot"/>, y esta es la de la raíz que el caso de uso
    /// está ESCRIBIENDO: durante una ACTUALIZACIÓN el árbol nuevo se prepara FUERA de
    /// la raíz activa, así que el confine se comprueba contra la raíz de preparación y
    /// no contra la que hay en el disco (AD-9, AC4, FR-11).
    /// </para>
    /// <para>
    /// La REGLA es la misma en las dos: la ruta canónica se comprueba, y el destino que
    /// sale de la raíz se rechaza (AD-9).
    /// </para>
    /// </summary>
    /// <param name="rootPath">Canonical absolute path of the root that confines the write.</param>
    /// <param name="relativePath">Path of the manifest, relative to that root.</param>
    /// <returns>The canonical absolute path that resolves inside the given root.</returns>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    /// <exception cref="InstallPathOutsideRootException">
    /// The resolved canonical path does NOT stay inside the given root (AD-9).
    /// </exception>
    string ResolveInside(string rootPath, string relativePath);

    /// <summary>
    /// Tells whether a relative path of the manifest resolves inside an EXPLICIT root,
    /// without throwing (AD-9, AC5).
    /// </summary>
    /// <param name="rootPath">Canonical absolute path of the root that confines the write.</param>
    /// <param name="relativePath">Path of the manifest, relative to that root.</param>
    /// <returns><c>true</c> when the path resolves inside the given root.</returns>
    bool IsInside(string rootPath, string relativePath);
}

/// <summary>
/// A path of the installation manifest resolves OUTSIDE the installation root of its
/// game (AD-9, FR-8).
///
/// <para>
/// El rechazo nombra la ruta canónica que lo intentó, porque el manifiesto puede
/// escribir la ruta de muchas formas y la que importa es la canónica (AD-9).
/// </para>
/// </summary>
public sealed class InstallPathOutsideRootException : Exception
{
    /// <summary>Creates the exception of a path that escapes the installation root.</summary>
    /// <param name="relativePath">Path of the manifest, relative to the installation root.</param>
    /// <param name="canonicalPath">Canonical absolute path that the path resolved to.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    public InstallPathOutsideRootException(
        string relativePath,
        string canonicalPath,
        string rootPath)
        : base($"La ruta '{relativePath}' del manifiesto de instalación resuelve en '{canonicalPath}', que está FUERA de la raíz de instalación '{rootPath}'.")
    {
        RelativePath = relativePath;
        CanonicalPath = canonicalPath;
        RootPath = rootPath;
    }

    /// <summary>Path of the manifest, relative to the installation root.</summary>
    public string RelativePath { get; }

    /// <summary>Canonical absolute path that the path resolved to.</summary>
    public string CanonicalPath { get; }

    /// <summary>Canonical absolute path of the installation root.</summary>
    public string RootPath { get; }
}
