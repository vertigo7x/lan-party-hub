using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;

namespace LANPartyHub.Client.Infrastructure.Filesystem;

/// <summary>
/// Adapter of the resolution of the paths of the installation (AD-9, AC1, AC3).
///
/// <para>
/// La resolución se hace sobre la RUTA CANÓNICA: la ruta del manifiesto es relativa a la
/// raíz configurada, se resuelve contra ella, y la ruta resultante se comprueba una vez
/// resueltos los enlaces simbólicos (AD-9).
/// </para>
/// <para>
/// El COMPRUEBO se repite antes de cada escritura, y este adaptador es el que lo hace:
/// una ruta que sale de la raíz se RECHAZA y el rechazo nombra la ruta canónica que lo
/// intentó (AD-9).
/// </para>
/// <para>
/// La raíz se LEE DEL LAYOUT en cada llamada, sin caché entre escrituras: una ruta que
/// era válida deja de serlo en cuanto la raíz se mueve o en cuanto aparece un enlace,
/// así que una copia congelada de la raíz dejaría pasar la escapada (AD-9, AC1).
/// </para>
/// <para>
/// La raíz es POR JUEGO: cada juego tiene la suya, así que el confine se comprueba
/// contra la raíz de ESE juego y una ruta que cae en la raíz de otro queda FUERA
/// (AD-9, FR-8).
/// </para>
/// </summary>
public sealed class InstallPathResolver : IInstallPathResolver
{
    private readonly IClientDataLayout _layout;

    /// <summary>
    /// Creates the resolver over the layout of the data folder of the client (AD-9).
    /// </summary>
    /// <param name="layout">Layout that holds the effective installation root.</param>
    /// <exception cref="ArgumentNullException">The layout is <c>null</c>.</exception>
    public InstallPathResolver(IClientDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _layout = layout;
    }

    /// <inheritdoc />
    public string GameRootPath(string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        // AD-9: la raíz del JUEGO se deriva del layout en cada llamada, sin caché: la
        // raíz que confine es la del MOMENTO, no una copia congelada (AD-9, AC1).
        //
        // La raíz vacía se rechaza de forma EXPLÍCITA: canonicalizarla dependería del
        // host (en Windows `GetFullPath("")` lanza, en Linux devuelve el directorio
        // actual), y una raíz vacía no es una raíz.
        var installDirectory = _layout.InstallDirectory;
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        return Canonicalize(_layout.GameInstallDirectory(gameId));
    }

    /// <inheritdoc />
    public string ResolveInsideGameRoot(string gameId, string relativePath) =>
        ResolveInside(GameRootPath(gameId), relativePath);

    /// <inheritdoc />
    public bool IsInsideGameRoot(string gameId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            ResolveInsideGameRoot(gameId, relativePath);

            return true;
        }
        catch (InstallPathOutsideRootException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string ResolveInside(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        // AD-9: la raíz se lee AHORA, no en el constructor: el confine se repite antes
        // de cada escritura y una raíz que se mueve tiene que surtir efecto de
        // inmediato.
        var root = Canonicalize(rootPath);

        // AD-9: el manifiesto NO admite rutas absolutas. Una ruta absoluta se resuelve
        // contra la raíz como si fuera relativa, así que la comprobación canónica la
        // rechaza si el resultado sale de la raíz.
        //
        // La ruta se reconoce como ABSOLUTA por su FORMA, no por la del host: el
        // manifiesto declara rutas relativas a la raíz, y una que trae letra de
        // unidad o UNC es absoluta con independencia de la plataforma que la
        // comprueba (AD-9).
        // Los separadores se NORMALIZAN antes de combinar: un manifiesto declara
        // rutas con cualquiera de los dos, y el host solo reconoce el suyo. Sin
        // esto, una ruta con `\\` sería en Linux un nombre de archivo literal y
        // no la ruta que el manifiesto declara (AD-9).
        var candidate = InstallPathConfine.IsAbsoluteByForm(relativePath)
            ? relativePath
            : Path.Combine(root, NormalizeSeparators(relativePath));

        var canonical = Canonicalize(candidate);

        // AD-9: la regla del confine es la del DOMINIO, y el adaptador la aplica: la
        // comparación es por prefijo CON separador final, así que una carpeta hermana
        // que comparte el nombre de la raíz queda fuera.
        if (InstallPathConfine.IsOutside(root, canonical))
        {
            throw new InstallPathOutsideRootException(relativePath, canonical, root);
        }

        return canonical;
    }

    /// <inheritdoc />
    public bool IsInside(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            ResolveInside(rootPath, relativePath);

            return true;
        }
        catch (InstallPathOutsideRootException)
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizes the SEPARATORS of a declared path to the ones of the host
    /// (AD-9).
    ///
    /// <para>
    /// Un manifiesto declara rutas relativas con cualquiera de los dos separadores, y
    /// el host solo reconoce el suyo: sin normalizar, en Linux una ruta con `\\` sería
    /// un nombre de archivo literal en lugar de la ruta que declara el manifiesto.
    /// </para>
    /// </summary>
    /// <param name="path">Path as the manifest wrote it.</param>
    /// <returns>The path with the separators of the host.</returns>
    private static string NormalizeSeparators(string path)
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            return path;
        }

        return path.Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Resolves a path to its CANONICAL form, resolving the symbolic links when the
    /// path exists (AD-9).
    ///
    /// <para>
    /// La resolución es del ANCESTRO EXISTENTE MÁS PROFUNDO: una ruta puede atravesar
    /// un enlace INTERMEDIO (un directorio o una unión) y salir de la raíz sin que el
    /// último segmento sea un enlace. Resolver sólo el último componente dejaría pasar
    /// esa escapada, así que la ruta entera se resuelve y los segmentos que aún no
    /// existen se reañaden sobre el destino ya resuelto (AD-9).
    /// </para>
    /// </summary>
    /// <param name="path">Path to canonicalize.</param>
    /// <returns>The canonical absolute path.</returns>
    private static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);

        // Un enlace simbólico se resuelve a su DESTINO, que es la ruta que de verdad
        // confine la escritura: comprobar la forma sin resolver dejaría pasar un enlace
        // que apunta fuera de la raíz (AD-9).
        try
        {
            var resolved = ResolveLinks(full);

            return resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (IOException)
        {
            // Una ruta que no existe todavía no puede tener un enlace: su forma
            // canónica es la forma absoluta.
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (UnauthorizedAccessException)
        {
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>
    /// Resolves the LINKS of a whole path: the deepest existing ancestor is resolved to
    /// its final target, and the segments that do not exist yet are re-appended to it
    /// (AD-9).
    ///
    /// <para>
    /// El ancestro se busca subiendo desde el final: el último segmento de una ruta de
    /// escritura aún no existe, así que el enlace que hay que resolver es el de un
    /// DIRECTORIO INTERMEDIO, no el del archivo final.
    /// </para>
    /// </summary>
    /// <param name="full">Absolute path to resolve.</param>
    /// <returns>The absolute path with every link resolved.</returns>
    private static string ResolveLinks(string full)
    {
        var root = Path.GetPathRoot(full);

        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var segments = full.Substring(root.Length)
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

        // Los PREFIXOS de la ruta, del más corto al más largo: cada uno es un
        // ancestro posible del segmento final.
        var prefixes = new string[segments.Length];
        var accumulated = root;

        for (var index = 0; index < segments.Length; index++)
        {
            accumulated = Path.Combine(accumulated, segments[index]);
            prefixes[index] = accumulated;
        }

        // Se sube hasta el ancestro existente MÁS PROFUNDO: los segmentos que no
        // existen todavía se guardan para reañadirlos sobre el destino resuelto.
        var pending = new List<string>(segments.Length);
        var current = root;

        for (var index = prefixes.Length - 1; index >= 0; index--)
        {
            var candidate = prefixes[index];

            if (!Exists(candidate))
            {
                pending.Insert(0, segments[index]);

                continue;
            }

            var resolved = ResolveFinalTarget(candidate);

            current = resolved ?? candidate;

            break;
        }

        foreach (var segment in pending)
        {
            current = Path.Combine(current, segment);
        }

        return Path.GetFullPath(current);
    }

    /// <summary>
    /// Tells whether a path exists, as a file or as a directory.
    /// </summary>
    /// <param name="path">Absolute path to check.</param>
    /// <returns><c>true</c> when the path exists.</returns>
    private static bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    /// <summary>
    /// Resolves a path that IS a link to its final target.
    /// </summary>
    /// <param name="path">Absolute path of an existing file or directory.</param>
    /// <returns>
    /// The absolute path of the final target, or <c>null</c> when the path is NOT a
    /// link.
    /// </returns>
    private static string? ResolveFinalTarget(string path)
    {
        var target = Directory.Exists(path)
            ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)
            : new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);

        return target is null ? null : Path.GetFullPath(target.FullName);
    }
}
