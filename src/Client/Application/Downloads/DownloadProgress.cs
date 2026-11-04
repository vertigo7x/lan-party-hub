using System.Globalization;

namespace LANPartyHub.Client.Application.Downloads;

/// <summary>
/// Progress of a download operation, and the SINGLE source of the percentage and of the
/// pair of sizes that the interface shows (AD-25, UX-DR-5, UX-DR-13).
///
/// <para>
/// La barra y el texto NUNCA se contradicen: los dos salen de esta misma fuente, así que
/// la fracción que la barra dibuja es EXACTAMENTE la que el porcentaje del texto
/// declara (UX-DR-5, UX-DR-13).
/// </para>
/// <para>
/// El porcentaje es siempre un ENTERO derivado de los bytes transferidos sobre los
/// totales, y el par de tamaños es «&lt;a&gt; GB de &lt;t&gt; GB» con el total SIEMPRE
/// segundo y los tamaños en gigabytes decimales con un solo decimal (UX-DR-5).
/// </para>
/// </summary>
public sealed class DownloadProgress
{
    /// <summary>Number of bytes of one decimal gigabyte (AD-7, UX-DR-5).</summary>
    public const long BytesPerGigabyte = 1_000_000_000L;

    /// <summary>
    /// Creates the progress of a download operation.
    /// </summary>
    /// <param name="bytesTransferred">Bytes that have been transferred so far.</param>
    /// <param name="totalBytes">
    /// Total bytes of the package, or <c>null</c> when the server has not declared them
    /// yet. While the total is unknown there is NO percentage: un porcentaje sobre un
    /// total que no se conoce sería una mentira (UX-DR-13).
    /// </param>
    public DownloadProgress(long bytesTransferred, long? totalBytes)
    {
        if (bytesTransferred < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesTransferred),
                "Los bytes transferidos no pueden ser negativos.");
        }

        if (totalBytes is { } total && total < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalBytes),
                "El total de bytes no puede ser negativo.");
        }

        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
    }

    /// <summary>Bytes that have been transferred so far.</summary>
    public long BytesTransferred { get; }

    /// <summary>
    /// Total bytes of the package, or <c>null</c> when the server has not declared them.
    /// </summary>
    public long? TotalBytes { get; }

    /// <summary>
    /// The whole percentage of the download, as an integer between zero and a hundred.
    ///
    /// <para>
    /// El porcentaje es NUNCA un decimal: el contrato lo declara entero y la barra lo
    /// dibuja con la misma fracción que este valor (UX-DR-5).
    /// </para>
    /// </summary>
    public int Percentage => TotalBytes is { } total && total > 0
        ? (int)Math.Clamp(BytesTransferred * 100 / total, 0, 100)
        : 0;

    /// <summary>
    /// The fraction that the bar draws, between zero and one.
    ///
    /// <para>
    /// La fracción es la MISMA que la del porcentaje, calculada con la misma división:
    /// la barra no puede decir el 42 % y dibujar el 43 % (UX-DR-5, UX-DR-13).
    /// </para>
    /// </summary>
    public double Fraction => Percentage / 100d;

    /// <summary>
    /// The pair of sizes of the download, as «&lt;a&gt; GB de &lt;t&gt; GB» (UX-DR-5).
    ///
    /// <para>
    /// El total va SIEMPRE segundo, y los dos tamaños llevan su unidad y un solo decimal
    /// con la coma decimal española (UX-DR-5).
    /// </para>
    /// </summary>
    public string SizesText => TotalBytes is { } total
        ? $"{FormatGigabytes(BytesTransferred)} GB de {FormatGigabytes(total)} GB"
        : $"{FormatGigabytes(BytesTransferred)} GB";

    /// <summary>
    /// The text of the progress, as «&lt;n&gt; % · &lt;a&gt; GB de &lt;t&gt; GB» (UX-DR-5).
    /// </summary>
    public string Text => TotalBytes is null
        ? $"{Percentage} % · {SizesText}"
        : $"{Percentage} % · {SizesText}";

    /// <summary>
    /// Formats a size in exact bytes into its decimal gigabytes, with exactly one decimal
    /// and the Spanish decimal separator (AD-7, UX-DR-5).
    ///
    /// <para>
    /// La regla es la del <c>SizeFormatter</c> de la capa de presentación, pero el
    /// coordinador la necesita para NOMBRAR lo que se pierde al cancelar, y el centro de
    /// la aplicación no puede depender de la presentación (AD-1). La regla es la misma y
    /// las dos copias se verifican por las mismas expectativas.
    /// </para>
    /// </summary>
    /// <param name="sizeBytes">Exact size in bytes.</param>
    /// <returns>The size, as for example <c>3,2</c>.</returns>
    public static string FormatGigabytes(long sizeBytes)
    {
        var negative = sizeBytes < 0;

        var absolute = negative ? -sizeBytes : sizeBytes;

        var whole = absolute / BytesPerGigabyte;
        var remainder = absolute % BytesPerGigabyte;

        // El décimo se redondea al más cercano con enteros, así que el redondeo nunca
        // depende de la precisión de un flotante, y la coma decimal es la española
        // (UX-DR-5).
        var tenths = (remainder * 10 + BytesPerGigabyte / 2) / BytesPerGigabyte;

        if (tenths == 10)
        {
            whole += 1;
            tenths = 0;
        }

        var sign = negative ? "-" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{whole},{tenths}");
    }
}
