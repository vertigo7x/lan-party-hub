using System.Globalization;
using System.Text;

namespace LANPartyHub.Server.Domain.Clients;

/// <summary>
/// Rules of the human label of the team, <c>machineName</c> (AD-30, AD-31).
///
/// <para>
/// The server is the VALIDATOR of the name: the client proposes it and the server
/// validates it, resolves the collisions and makes it unique in the registry
/// (AD-30). The rules live here, in the domain, and they are PURE: they depend on the
/// text that they receive and never on the transport, so they can be exercised
/// without a server and without a network (AD-1).
/// </para>
/// <para>
/// The length of a name is measured in UNICODE CODE POINTS
/// (<see cref="System.Text.Rune"/>), never in bytes and never in UTF-16 code units,
/// so the limit of 64 describes the same amount of VISIBLE characters that the
/// organizer sees in the dense table of clients (AD-30, AD-31).
/// </para>
/// </summary>
public static class MachineNamePolicy
{
    /// <summary>
    /// Maximum length of a name, in Unicode code points (AD-30).
    /// </summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// Maximum length of the STEM of a derived name, in Unicode code points (AD-31).
    ///
    /// <para>
    /// The suffix of a derived name has a FIXED length of 33 code points, so the stem
    /// is always trimmed to fit in the remaining 31 (AD-31).
    /// </para>
    /// </summary>
    public const int MaximumDerivedStemLength = 31;

    /// <summary>
    /// Length of the suffix of a derived name, in Unicode code points: the hyphen and
    /// the 32 hexadecimal digits of the canonical form of the client identifier
    /// (AD-31).
    /// </summary>
    public const int DerivedSuffixLength = 33;

    /// <summary>
    /// Stem of reserve used when the trim of the proposed name would leave it empty
    /// (AD-31).
    /// </summary>
    public const string ReserveStem = "equipo";

    /// <summary>
    /// Tells whether the proposed name satisfies every rule of shape of AD-30.
    ///
    /// <para>
    /// The rules are: the name is present and not empty once trimmed; its length is
    /// between 1 and 64 code points; it carries no control character; it carries no
    /// leading, trailing or consecutive space; and every character belongs to the
    /// admitted set of letters, digits, <c>.</c>, <c>-</c>, <c>_</c> and a single
    /// inner space (AD-30).
    /// </para>
    /// </summary>
    /// <param name="machineName">Proposed name of the team.</param>
    /// <returns><c>true</c> when the name satisfies every rule of shape.</returns>
    public static bool IsValid(string? machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName))
        {
            return false;
        }

        // El texto ORIGINAL es el que se examina: recortar ANTES de comprobar los
        // espacios y los controles los escondería, y un nombre con un espacio inicial
        // o un salto de línea tiene que ser rechazado (AD-30).
        var codePoints = EnumerateRunes(machineName).ToArray();

        if (codePoints.Length == 0 || codePoints.Length > MaximumLength)
        {
            return false;
        }

        var previousWasSpace = false;
        var isFirst = true;

        foreach (var codePoint in codePoints)
        {
            if (Rune.IsControl(codePoint))
            {
                return false;
            }

            if (codePoint.Value == ' ')
            {
                // A leading space, a trailing space and two consecutive spaces are all
                // rejected: the name lives in a column of fixed width and the double
                // space produces identifiers that cannot be told apart (AD-30).
                if (isFirst || previousWasSpace)
                {
                    return false;
                }

                previousWasSpace = true;

                continue;
            }

            isFirst = false;
            previousWasSpace = false;

            if (!IsAdmitted(codePoint))
            {
                return false;
            }
        }

        // El espacio final ya lo rechaza la regla de los espacios consecutivos cuando
        // hay dos, así que el trailing space se comprueba aparte.
        return !previousWasSpace;
    }

    /// <summary>
    /// Computes the CANONICAL form of a name, which is the form that the uniqueness
    /// of the name compares (AD-30).
    ///
    /// <para>
    /// The canonical form is computed in three deterministic steps and in this order:
    /// Unicode NFKC, removal of the diacritical marks of the categories
    /// <c>Mn</c>, <c>Mc</c> and <c>Me</c>, and finally the invariant Unicode
    /// lowercase. The order matters: NFKC collapses the compatibility variants
    /// first and avoids that two visually identical strings end up with different
    /// canonical forms (AD-30).
    /// </para>
    /// <para>
    /// La eliminación de las marcas va PRECEDIDA de la descomposición canónica
    /// (NFD), porque NFKC COMBINA las marcas con su letra: sin la descomposición, la
    /// marca de una «é» precompuesta nunca llega a ser una marca y la comparación no
    /// distinguiría «equipo» de «équipo». La descomposición es CANÓNICA y no altera
    /// el texto, así que el orden normativo de AD-30 se respeta: primero se colapsan
    /// las variantes de compatibilidad, y solo después se eliminan las marcas
    /// (AD-30).
    /// </para>
    /// </summary>
    /// <param name="machineName">Name of the team.</param>
    /// <returns>The canonical form of the name.</returns>
    public static string Canonicalize(string? machineName)
    {
        if (string.IsNullOrEmpty(machineName))
        {
            return string.Empty;
        }

        var normalized = machineName.Normalize(NormalizationForm.FormKC);

        var decomposed = normalized.Normalize(NormalizationForm.FormD);

        var withoutMarks = new StringBuilder(decomposed.Length);

        foreach (var codePoint in EnumerateRunes(decomposed))
        {
            if (IsDiacriticalMark(codePoint))
            {
                continue;
            }

            withoutMarks.Append(codePoint.ToString());
        }

        return withoutMarks.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Derives the EFFECTIVE name of a team whose proposed name collides with the one
    /// of another registered client (AD-31).
    ///
    /// <para>
    /// The effective name is <c>stem_truncado</c> plus the fixed suffix <c>-</c>
    /// followed by the 32 lowercase hexadecimal digits of the canonical form of the
    /// client identifier, <c>Guid.ToString("N")</c>. The suffix has a FIXED length of
    /// 33 code points, so the stem is always trimmed to at most 31 code points and the
    /// result NEVER exceeds the limit of 64 (AD-31).
    /// </para>
    /// <para>
    /// The derivation is a PURE function of (<c>stem</c>, <c>clientId</c>): the
    /// server never looks for a free name, never increments a counter and never tries
    /// successive candidates, so it can never exhaust the space of names (AD-31).
    /// The trim happens by code points and NEVER splits one in half, so an emoji
    /// survives it (AD-31).
    /// </para>
    /// </summary>
    /// <param name="proposedName">Proposed name of the team, already trimmed.</param>
    /// <param name="clientId">Canonical identifier of the client.</param>
    /// <returns>The effective name of the team.</returns>
    public static string Derive(string? proposedName, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var stem = TrimToCodePoints(proposedName ?? string.Empty, MaximumDerivedStemLength);

        if (stem.Length == 0)
        {
            stem = ReserveStem;
        }

        return $"{stem}-{clientId.ToLowerInvariant()}";
    }

    /// <summary>
    /// Names the concrete rule of shape that a proposed name does not satisfy, so that
    /// the organizer knows what to correct without guessing (AD-25).
    ///
    /// <para>
    /// La regla y su MOTIVO salen del MISMO sitio, así que no pueden divergir: la
    /// validación y el diagnóstico son una sola regla (AD-30, AD-25).
    /// </para>
    /// </summary>
    /// <param name="machineName">Proposed name of the team.</param>
    /// <returns>
    /// The rule that the name breaks, or <c>null</c> when the name satisfies every rule.
    /// </returns>
    public static string? DescribeShapeFailure(string? machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName))
        {
            return "está vacío";
        }

        var codePoints = EnumerateRunes(machineName).ToArray();

        if (codePoints.Length > MaximumLength)
        {
            return $"supera los {MaximumLength} puntos de código admitidos";
        }

        var previousWasSpace = false;
        var isFirst = true;

        foreach (var codePoint in codePoints)
        {
            if (Rune.IsControl(codePoint))
            {
                return "contiene un carácter de control";
            }

            if (codePoint.Value == ' ')
            {
                if (isFirst)
                {
                    return "empieza con un espacio";
                }

                if (previousWasSpace)
                {
                    return "contiene espacios consecutivos";
                }

                previousWasSpace = true;

                continue;
            }

            isFirst = false;
            previousWasSpace = false;

            if (!IsAdmitted(codePoint))
            {
                return "contiene un carácter fuera del conjunto admitido";
            }
        }

        if (previousWasSpace)
        {
            return "termina con un espacio";
        }

        return null;
    }

    /// <summary>
    /// Tells whether a code point belongs to the set that AD-30 admits: letters,
    /// digits, <c>.</c>, <c>-</c>, <c>_</c> and the inner space.
    /// </summary>
    /// <param name="codePoint">Code point to check.</param>
    /// <returns><c>true</c> when the code point is admitted.</returns>
    private static bool IsAdmitted(Rune codePoint)
    {
        if (codePoint.Value is ' ' or '.' or '-' or '_')
        {
            return true;
        }

        // La categoría Unicode decide si un ideograma o un emoji forman parte del set
        // admitido: son letras, y ninguno de los dos es un carácter de control
        // (AD-30).
        return Rune.IsLetter(codePoint) || Rune.IsDigit(codePoint);
    }

    /// <summary>
    /// Tells whether a code point is a diacritical mark of the categories
    /// <c>Mn</c>, <c>Mc</c> or <c>Me</c>, which the canonical form removes (AD-30).
    /// </summary>
    /// <param name="codePoint">Code point to check.</param>
    /// <returns><c>true</c> when the code point is a diacritical mark.</returns>
    private static bool IsDiacriticalMark(Rune codePoint) =>
        Rune.GetUnicodeCategory(codePoint) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// Trims a text to at most the given number of Unicode code points, cutting at the
    /// END and never in the middle of a code point (AD-31).
    /// </summary>
    /// <param name="value">Text to trim.</param>
    /// <param name="maximumLength">Maximum number of code points of the result.</param>
    /// <returns>The trimmed text.</returns>
    private static string TrimToCodePoints(string value, int maximumLength)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var count = 0;

        foreach (var codePoint in EnumerateRunes(value))
        {
            if (count == maximumLength)
            {
                break;
            }

            builder.Append(codePoint.ToString());

            count++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Enumerates the code points of a text, so that a length is never measured in
    /// UTF-16 code units (AD-30, AD-31).
    /// </summary>
    /// <param name="value">Text to enumerate.</param>
    /// <returns>The code points of the text.</returns>
    private static IEnumerable<Rune> EnumerateRunes(string value)
    {
        for (var index = 0; index < value.Length;)
        {
            var rune = Rune.DecodeFromUtf16(value.AsSpan(index), out Rune decoded, out int charsConsumed);

            if (charsConsumed == 0)
            {
                yield break;
            }

            yield return decoded;

            index += charsConsumed;
        }
    }
}
