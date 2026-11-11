namespace LANPartyHub.Client.Domain.Install;

/// <summary>
/// The confinement of the paths of the installation (AD-9, NFR-5, AC1, AC3).
///
/// <para>
/// La regla es PURA: recibe la ruta canónica de la raíz y la ruta canónica de un
/// destino, y dice si el destino queda DENTRO de la raíz. No toca el disco, no conoce
/// el adaptador y no depende de nadie, así que el confine es un caso unitario (AD-16).
/// </para>
/// <para>
/// La comparación es por PREFIJO CON SEPARADOR FINAL: la raíz <c>C:\juegos</c> no
/// confina a <c>C:\juegos-evil</c>, porque el prefijo de texto coincide pero el
/// separador no. La comparación es <c>OrdinalIgnoreCase</c>, porque el único objetivo
/// del cliente es Windows y allí <c>C:\Juegos</c> y <c>c:\juegos</c> son la MISMA
/// carpeta (AD-17).
/// </para>
/// <para>
/// La regla se aplica sobre la ruta CANÓNICA, que es la que tiene los enlaces simbólicos
/// y las uniones ya resueltos: comprobar la forma que escribió el manifiesto dejaría
/// pasar un enlace que apunta fuera de la raíz (AD-9).
/// </para>
/// </summary>
public static class InstallPathConfine
{
    /// <summary>
    /// Tells whether a path is ABSOLUTE by its own form, whatever the host is
    /// (AD-9, AC2).
    ///
    /// <para>
    /// La forma absoluta se reconoce por SUYO: una letra de unidad seguida de dos
    /// puntos (<c>C:\</c>), una ruta que empieza por separador (<c>/</c>), o un UNC
    /// (<c>\\servidor\recurso</c>). El host puede reconocer mas formas de las que
    /// esta, pero no menos: una ruta que es absoluta para el sistema que la escribio
    /// lo es para el que la comprueba, y el manifiesto puede declarar rutas de
    /// Windows aunque la comprobación corra en otra plataforma (AD-9).
    /// </para>
    /// </summary>
    /// <param name="path">Path as it was written.</param>
    /// <returns><c>true</c> when the path is absolute by its form.</returns>
    public static bool IsAbsoluteByForm(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Una unidad: `C:` o `C:\...`. Se admite CUALQUIER carácter antes de los dos
        // puntos, como hace Windows, para que la regla no dependa del host: una ruta
        // que Windows da por absoluta tiene que ser absoluta para esta regla (AD-9).
        if (path.Length >= 2 && path[1] == ':')
        {
            return true;
        }

        // Un UNC: `\\servidor\recurso`.
        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
        {
            return true;
        }

        // Una ruta que empieza por separador.
        return path[0] == '/' || path[0] == '\\';
    }

    /// <summary>
    /// Tells whether a canonical path stays INSIDE a canonical root (AD-9).
    ///
    /// <para>
    /// La raíz misma es un destino LEGAL: escribir en la raíz no sale de la raíz.
    /// </para>
    /// </summary>
    /// <param name="root">Canonical absolute path of the installation root.</param>
    /// <param name="path">Canonical absolute path of the destination.</param>
    /// <returns><c>true</c> when the path is the root or is inside it.</returns>
    public static bool IsInside(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedRoot = Normalize(root);
        var normalizedPath = Normalize(path);

        if (string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // AD-9: el prefijo lleva el separador final, así que una carpeta hermana que
        // comparte el NOMBRE de la raíz (`C:\juegos` contra `C:\juegos-evil`) queda
        // FUERA: el confine compara la RUTA, no un prefijo de texto.
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether a canonical path stays OUTSIDE a canonical root (AD-9, AC3).
    ///
    /// <param name="root">Canonical absolute path of the installation root.</param>
    /// <param name="path">Canonical absolute path of the destination.</param>
    /// <returns><c>true</c> when the path does NOT stay inside the root.</returns>
    public static bool IsOutside(string root, string path) => !IsInside(root, path);

    /// <summary>
    /// Normalizes a canonical path for the comparison of the confine: the trailing
    /// separator is removed, so the root and the root with a trailing separator are the
    /// SAME root (AD-9).
    /// </summary>
    /// <param name="path">Canonical absolute path.</param>
    /// <returns>The normalized path.</returns>
    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
