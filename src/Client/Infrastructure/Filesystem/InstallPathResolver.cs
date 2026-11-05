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
            var resolved = Path.GetFullPath(
                new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full);

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
