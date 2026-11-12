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
    /// Suffix of the folder of PREPARATION of a game (AD-9, AC3, FR-11).
    /// </summary>
    /// <remarks>
    /// La raíz de preparación es HERMANA de la raíz del juego y nunca una carpeta
    /// dentro: una carpeta dentro no se puede intercambiar con su madre, y el
    /// intercambio por nombre es lo que deja la instalación anterior intacta (AD-9).
    /// </remarks>
    public const string PreparationSuffix = ".preparacion";

    /// <summary>
    /// Suffix of the folder that holds the root REPLACED, while the new one takes its
    /// place (AD-9, AC3, FR-11).
    /// </summary>
    public const string ReplacedSuffix = ".sustituida";

    /// <summary>
    /// The characters that WINDOWS does not admit in a file name, and that the host may
    /// admit (AD-9, AD-17).
    /// </summary>
    /// <remarks>
    /// El conjunto de Windows COMPLETO, con los caracteres de control del 0x00 al
    /// 0x1F incluidos: en Linux el host sólo rechaza el 0x00 y la barra, así que sin
    /// ellos el conjunto de la regla no sería el de la plataforma del cliente.
    /// </remarks>
    internal static readonly char[] WindowsInvalidFileNameChars =
        new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' }
            .Concat(Enumerable.Range(0, 32).Select(index => (char)index))
            .ToArray();

    /// <summary>
    /// The characters that are NOT valid in the name of a FOLDER, for the host AND for
    /// WINDOWS (AD-9, AD-17).
    /// </summary>
    /// <remarks>
    /// El cliente es una aplicación de Windows (AD-17), así que el identificador de un
    /// juego se usa como NOMBRE DE CARPETA y no como nombre de archivo suelto: un `*` es
    /// un nombre válido en Linux y no lo es en Windows, y la carpeta donde vive el árbol
    /// del juego se crea en la plataforma del cliente.
    /// </remarks>
    private static readonly char[] InvalidFolderNameChars =
        Path.GetInvalidFileNameChars()
            .Concat(WindowsInvalidFileNameChars)
            .Distinct()
            .ToArray();

    /// <summary>
    /// Derives the installation root of a GAME from the base install directory
    /// (AD-9, FR-8).
    ///
    /// <para>
    /// La raíz de cada juego es una CARPETA HIJA de la base de instalación, con el
    /// identificador del juego como nombre: la base agrupa los juegos y cada juego
    /// tiene su propia raíz, así que un juego no puede leer, escribir, sustituir ni
    /// borrar el árbol de otro (AD-9, FR-8).
    /// </para>
    /// <para>
    /// El identificador del juego se usa como NOMBRE DE CARPETA, así que se comprueba
    /// antes de combinarlo: un identificador con un separador o con <c>..</c> saldría
    /// de la base, y una raíz que sale de la base no confine nada (AD-9).
    /// </para>
    /// </summary>
    /// <param name="baseDirectory">Absolute path of the base install directory.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns>The absolute path of the installation root of that game.</returns>
    /// <exception cref="ArgumentException">
    /// The base directory is empty, or the identifier cannot be used as a folder name.
    /// </exception>
    public static string GameRoot(string baseDirectory, string gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        if (!IsValidGameId(gameId))
        {
            throw new ArgumentException(
                "El identificador de un juego no puede usarse como nombre de carpeta.",
                nameof(gameId));
        }

        return Path.Combine(baseDirectory, gameId);
    }

    /// <summary>
    /// Tells whether an identifier of a videogame can be used as the name of the
    /// FOLDER that holds its installation (AD-9).
    ///
    /// <para>
    /// La comprobación vive aquí y no en cada sitio que usa el identificador como
    /// nombre de carpeta: el resolver y el layout la usan los dos, y con dos conjuntos
    /// distintos un identificador podía pasar por uno y ser rechazado por el otro.
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <returns><c>true</c> when the identifier can be used as a folder name.</returns>
    public static bool IsValidGameId(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        // La carpeta actual y la carpeta padre NO son raíces de juego: la raíz del
        // juego es SIEMPRE una carpeta propia, así que un identificador que las nombra
        // se rechaza (AD-9).
        if (string.Equals(gameId, ".", StringComparison.Ordinal)
            || string.Equals(gameId, "..", StringComparison.Ordinal))
        {
            return false;
        }

        // El sufijo RESERVADO de la raíz hermana es parte del nombre de la carpeta del
        // juego: un identificador que lo terminara NOMBRARÍA la raíz de preparación de
        // otro juego, y la sustitución de ese otro la tocaría (AD-9, FR-8).
        if (gameId.EndsWith(PreparationSuffix, StringComparison.OrdinalIgnoreCase)
            || gameId.EndsWith(ReplacedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return gameId.IndexOfAny(InvalidFolderNameChars) < 0;
    }

    /// <summary>
    /// Tells whether a canonical path stays INSIDE the installation root of a GAME
    /// (AD-9, FR-8, NFR-5).
    ///
    /// <para>
    /// El confine se evalúa contra la raíz del JUEGO y no contra la carpeta compartida:
    /// la base de instalación agrupa los juegos, así que confines contra ella dejarían
    /// pasar la escritura en el árbol de otro (AD-9, FR-8).
    /// </para>
    /// </summary>
    /// <param name="baseDirectory">Absolute path of the base install directory.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="canonicalPath">Canonical absolute path of the destination.</param>
    /// <returns><c>true</c> when the path is the root of that game or is inside it.</returns>
    public static bool IsInsideGameRoot(
        string baseDirectory,
        string gameId,
        string canonicalPath) =>
        IsInside(GameRoot(baseDirectory, gameId), canonicalPath);

    /// <summary>
    /// Tells whether a path is ABSOLUTE by its own form, whatever the host is
    /// (AD-9, AC2).
    ///
    /// <para>
    /// La forma absoluta se reconoce por SUYO: una letra de unidad seguida de dos
    /// puntos (<c>C:\</c>) o una ruta que empieza por separador (<c>/</c>), que
    /// también cubre un UNC (<c>\\servidor\recurso</c>). La regla es la MISMA que
    /// aplica Windows, para que una ruta sea absoluta en una plataforma y en la otra a
    /// la vez: el manifiesto puede declarar rutas de Windows aunque la comprobación
    /// corra en otra plataforma (AD-9).
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

        // Una UNIDAD: `C:` o `C:\...`. La letra de la unidad tiene que ser una letra
        // ASCII, que es lo que Windows acepta: `Path.IsPathRooted` de .NET exige una
        // letra de unidad valida, así que `1:\x` NO es una ruta absoluta para Windows
        // y esta regla tampoco lo trata como tal (AD-9).
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        // Una ruta que empieza por separador. Un UNC (`\\servidor\recurso`) empieza
        // por dos, así que esta comprobación lo cubre tambien.
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
