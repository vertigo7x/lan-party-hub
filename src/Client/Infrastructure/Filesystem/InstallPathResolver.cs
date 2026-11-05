using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Filesystem;

/// <summary>
/// Adapter of the resolution of the paths of the installation (AD-9, AC5).
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
/// </summary>
public sealed class InstallPathResolver : IInstallPathResolver
{
    private readonly string _rootPath;

    /// <summary>
    /// Creates the resolver over the configured installation root (AD-9).
    /// </summary>
    /// <param name="rootPath">Absolute path of the installation root.</param>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    public InstallPathResolver(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Canonicalize(rootPath);
    }

    /// <inheritdoc />
    public string RootPath => _rootPath;

    /// <inheritdoc />
    public string ResolveInsideRoot(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        // AD-9: el manifiesto NO admite rutas absolutas. Una ruta absoluta se resuelve
        // contra la raíz como si fuera relativa, así que la comprobación canónica la
        // rechaza si el resultado sale de la raíz.
        var candidate = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_rootPath, relativePath);

        var canonical = Canonicalize(candidate);

        if (!IsInside(_rootPath, canonical))
        {
            throw new InstallPathOutsideRootException(relativePath, canonical, _rootPath);
        }

        return canonical;
    }

    /// <inheritdoc />
    public bool IsInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            ResolveInsideRoot(relativePath);

            return true;
        }
        catch (InstallPathOutsideRootException)
        {
            return false;
        }
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

    /// <summary>
    /// Tells whether a canonical path is inside a canonical folder. The root itself is
    /// INSIDE, because the root is a legal destination (AD-9).
    /// </summary>
    /// <param name="root">Canonical path of the root.</param>
    /// <param name="path">Canonical path to check.</param>
    /// <returns><c>true</c> when the path is the root or is inside it.</returns>
    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedRoot, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
