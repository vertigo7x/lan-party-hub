using System.Globalization;

namespace LANPartyHub.Client.Presentation.Formatting;

/// <summary>
/// Formatter of the size of a videogame in the client (AD-7, AC2).
///
/// <para>
/// The size is stored in exact bytes and the conversion to decimal gigabytes is a rule of
/// presentation, not of the model (AD-7). The rule is single: every size is expressed in
/// decimal gigabytes (10^9 bytes), with the Spanish decimal separator and with exactly one
/// decimal. There is no MiB and no GiB, no two decimals and no size without its unit
/// (AC2).
/// </para>
/// <para>
/// The numeric value and the unit are two separate parts, so the view can put the number
/// in the token of sizes and the unit in the small token of metadata (AC2, UX-DR-5).
/// </para>
/// <para>
/// The rule is the SAME that the server applies in its own formatter, because AD-18
/// limits <c>Shared</c> to contracts and the rule is a rule of presentation. The two
/// copies are verified by the same expectations, so the ficha of the client and the list
/// of the organizer never tell the player two different sizes (AD-18).
/// </para>
/// <para>
/// The formatting does not depend on the ambient culture of the machine: the separator is
/// the Spanish comma and the decimal point never reaches the text.
/// </para>
/// </summary>
public static class SizeFormatter
{
    /// <summary>Unit of the size of a videogame: decimal gigabytes (AD-7).</summary>
    public const string Unit = "GB";

    /// <summary>Number of bytes of one decimal gigabyte (AD-7).</summary>
    public const long BytesPerGigabyte = 1_000_000_000L;

    /// <summary>
    /// Formats a size in exact bytes into its decimal gigabytes, with exactly one decimal
    /// and the Spanish decimal separator.
    /// </summary>
    /// <param name="sizeBytes">Exact size in bytes.</param>
    /// <returns>The size, as for example <c>7,6 GB</c>.</returns>
    public static string Format(long sizeBytes)
    {
        var (value, _) = Split(sizeBytes);

        return $"{value} {Unit}";
    }

    /// <summary>
    /// Formats the numeric part of a size, without the unit. It is the part that goes in
    /// the monospaced token of sizes, and it carries the Spanish decimal separator with
    /// exactly one decimal (AC2, UX-DR-5).
    /// </summary>
    /// <param name="sizeBytes">Exact size in bytes.</param>
    /// <returns>The numeric part of the size, as for example <c>7,6</c>.</returns>
    public static string FormatValue(long sizeBytes)
    {
        var (value, _) = Split(sizeBytes);

        return value;
    }

    /// <summary>
    /// Splits a size in exact bytes into its numeric part, with exactly one decimal and the
    /// Spanish decimal separator, and its unit.
    /// </summary>
    /// <param name="sizeBytes">Exact size in bytes.</param>
    /// <returns>The numeric part and the unit of the size.</returns>
    public static (string Value, string Unit) Split(long sizeBytes)
    {
        // A negative size cannot exist in the catalog, but the formatter does not throw on
        // it: it shows the sign and the rest of the value, so the interface never hides a
        // value that arrived wrong.
        var negative = sizeBytes < 0;

        var absolute = negative ? -sizeBytes : sizeBytes;

        // The integer part is the whole number of decimal gigabytes and the decimal part
        // is the tenth of a gigabyte that remains. The remainder is computed with
        // integers, so the rounding never depends on the precision of a float.
        var whole = absolute / BytesPerGigabyte;
        var remainder = absolute % BytesPerGigabyte;

        // The tenth is the remainder expressed in tenths of a gigabyte, and it is rounded
        // to the nearest one: a remainder of exactly half a tenth goes up, so the same
        // rule is applied in every direction.
        var tenths = (remainder * 10 + BytesPerGigabyte / 2) / BytesPerGigabyte;

        if (tenths == 10)
        {
            whole += 1;
            tenths = 0;
        }

        var wholeText = whole.ToString(CultureInfo.InvariantCulture);
        var sign = negative ? "-" : string.Empty;

        // The separator is written by hand and not by a formatter of the culture, so the
        // text of the interface is the same in every machine, whatever its culture is.
        return ($"{sign}{wholeText},{tenths}", Unit);
    }
}
