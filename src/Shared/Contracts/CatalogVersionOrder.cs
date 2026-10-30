namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Order of the versions of the catalog (AD-10, AR-5).
///
/// <para>
/// The version of a videogame is an OPAQUE string that the editor writes, and the catalog
/// declares the order of its versions: the comparison of two versions NEVER uses the
/// lexicographic order of the text, because that order says that «1.10» is older than
/// «1.9» (AC3, AR-5).
/// </para>
/// <para>
/// The rule is SINGLE and it lives here, in <c>Shared</c>, so that the client and the
/// server derive the same view state of the same game: the version is split into its
/// segments by the dot, every segment is compared as a NUMBER and the first segment that
/// differs decides; a version with more segments is newer than the same version with less
/// segments (AC3).
/// </para>
/// <para>
/// The rule is tolerant with the strings that are not numbers: a segment that is not a
/// number is compared as a TEXT, and two of them are compared lexicographically. The
/// catalog never rejects a version for that, because the version is opaque (AD-10).
/// </para>
/// </summary>
public static class CatalogVersionOrder
{
    /// <summary>Character that separates the segments of a version.</summary>
    public const char SegmentSeparator = '.';

    /// <summary>
    /// Compares two versions with the semantic order of the catalog (AC3, AR-5).
    ///
    /// <param name="left">First version.</param>
    /// <param name="right">Second version.</param>
    /// <returns>
    /// A negative number when <paramref name="left"/> is older, zero when both are the
    /// same version and a positive number when <paramref name="left"/> is newer.
    /// </returns>
    public static int Compare(string? left, string? right)
    {
        var leftSegments = Segments(left);
        var rightSegments = Segments(right);

        var shared = Math.Min(leftSegments.Count, rightSegments.Count);

        for (var index = 0; index < shared; index++)
        {
            var comparison = CompareSegments(leftSegments[index], rightSegments[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        // La versión con más segmentos es la más nueva: «1.2» es anterior a «1.2.1».
        return leftSegments.Count.CompareTo(rightSegments.Count);
    }

    /// <summary>
    /// Tells whether a version is older than another one (AC3, AR-5).
    ///
    /// <param name="left">First version.</param>
    /// <param name="right">Second version.</param>
    /// <returns>Whether <paramref name="left"/> is older than <paramref name="right"/>.</returns>
    public static bool IsOlder(string? left, string? right) => Compare(left, right) < 0;

    /// <summary>
    /// Tells whether a version is newer than another one (AC3, AR-5).
    ///
    /// <param name="left">First version.</param>
    /// <param name="right">Second version.</param>
    /// <returns>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</returns>
    public static bool IsNewer(string? left, string? right) => Compare(left, right) > 0;

    /// <summary>
    /// Splits a version into its segments, dropping the empty ones and the spaces around
    /// them.
    /// </summary>
    /// <param name="version">Version to split.</param>
    /// <returns>The segments of the version.</returns>
    private static IReadOnlyList<string> Segments(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return [];
        }

        return version
            .Split(SegmentSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Compares two segments of a version: as numbers when both of them are numbers, and
    /// as texts otherwise (AD-10).
    /// </summary>
    /// <param name="left">First segment.</param>
    /// <param name="right">Second segment.</param>
    /// <returns>The comparison of the two segments.</returns>
    private static int CompareSegments(string left, string right)
    {
        var leftIsNumber = long.TryParse(left, System.Globalization.CultureInfo.InvariantCulture, out var leftNumber);

        var rightIsNumber = long.TryParse(right, System.Globalization.CultureInfo.InvariantCulture, out var rightNumber);

        if (leftIsNumber && rightIsNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (leftIsNumber)
        {
            return 1;
        }

        if (rightIsNumber)
        {
            return -1;
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }
}
