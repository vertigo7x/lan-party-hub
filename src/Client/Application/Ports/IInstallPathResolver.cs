namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the resolution of the paths of the installation (AD-9, AC1, AC3).
///
/// <para>
/// El port existe para que el caso de uso de la instalación pueda pronunciarse sobre el
/// CONFINÉ de las rutas sin conocer el disco: la resolución canónica y la comprobación
/// de que la ruta queda dentro de la raíz son una regla de AD-9 y el adaptador la
/// aplica contra el sistema de archivos (AD-1, AD-9).
/// </para>
/// <para>
/// La raíz se LEE DEL LAYOUT en cada llamada, sin caché entre escrituras: el confine se
/// repite antes de cada escritura y una raíz que se mueve tiene que surtir efecto de
/// inmediato, porque una ruta que era válida deja de serlo en cuanto la raíz cambia
/// (AD-9, AC1).
/// </para>
/// <para>
/// Toda ruta del manifiesto es RELATIVA a la raíz de instalación: el manifiesto no
/// admite rutas absolutas (AD-9, AC2).
/// </para>
/// </summary>
public interface IInstallPathResolver
{
    /// <summary>
    /// Canonical absolute path of the installation root that confines the installation
    /// (AD-9).
    ///
    /// <para>
    /// Es el valor DEL MOMENTO: la raíz se lee del layout en cada llamada, así que este
    /// valor refleja la configuración vigente, no una copia congelada (AD-9).
    /// </para>
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Resolves a relative path of the manifest against the installation root and
    /// REQUIRES that the resolved canonical path stays inside the root (AD-9).
    ///
    /// <para>
    /// Un destino fuera de la raíz se RECHAZA, y el rechazo nombra la ruta que lo
    /// intentó: la ruta canónica se comprueba, y no la forma que el manifiesto escribió
    /// (AD-9).
    /// </para>
    /// </summary>
    /// <param name="relativePath">Path of the manifest, relative to the installation root.</param>
    /// <returns>The canonical absolute path that resolves inside the root.</returns>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    /// <exception cref="InstallPathOutsideRootException">
    /// The resolved canonical path does NOT stay inside the installation root (AD-9).
    /// </exception>
    string ResolveInsideRoot(string relativePath);

    /// <summary>
    /// Tells whether a relative path of the manifest resolves inside the installation
    /// root, without throwing (AD-9, AC5).
    /// </summary>
    /// <param name="relativePath">Path of the manifest, relative to the installation root.</param>
    /// <returns><c>true</c> when the path resolves inside the root.</returns>
    bool IsInsideRoot(string relativePath);

    /// <summary>
    /// Resolves a relative path of the manifest against an EXPLICIT root and REQUIRES
    /// that the resolved canonical path stays inside THAT root (AD-9, AC5).
    ///
    /// <para>
    /// La resolución contra la raíz CONFIGURED es la de <see cref="ResolveInsideRoot"/>, y
    /// esta es la de la raíz que el caso de uso está ESCRIBIENDO: durante una
    /// ACTUALIZACIÓN el árbol nuevo se prepara FUERA de la raíz activa, así que el confine
    /// se comprueba contra la raíz de preparación y no contra la que hay en el disco
    /// (AD-9, AC4, FR-11).
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
/// A path of the installation manifest resolves OUTSIDE the configured installation root
/// (AD-9).
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
