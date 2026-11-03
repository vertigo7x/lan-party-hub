using System.Globalization;
using System.Text;
using LANPartyHub.Shared.Contracts;
using QRCoder;

namespace LANPartyHub.Server.Presentation.Trust;

/// <summary>
/// Exporter of the código QR del ancla de confianza (AC1, AD-27).
///
/// <para>
/// AD-27: el QR es una de las DOS vías out-of-band que admite el MVP, y lleva el
/// fingerprint del ancla y la dirección del servidor. El cliente lo escanea (o el usuario
/// teclea el fingerprint que el QR codifica) y el usuario CONTRASTA el valor con lo que
/// ve en la pantalla del organizador antes de confirmarlo (AD-27).
/// </para>
/// <para>
/// El QR no sustituye al contraste: es el TRANSPORTE del valor, y la verificación es la
/// mirada humana del jugador. Un intermediario que intercepta el QR sigue sin poder
/// convencer al jugador si este compara el valor con la pantalla del organizador
/// (AD-27).
/// </para>
/// <para>
/// La biblioteca QRCoder entra aislada detrás de este exportador, de modo que sus tipos
/// nunca salen de <c>src/Server/Presentation/Trust</c> y el resto de la interfaz no
/// depende de ella (AD-1).
/// </para>
/// </summary>
public static class TrustAnchorQrExporter
{
    /// <summary>
    /// Scheme of the payload that the QR carries (AD-27).
    /// </summary>
    public const string Scheme = "lanpartyhub://anchor";

    /// <summary>
    /// Name of the parameter that carries the fingerprint of the ancla (AD-27).
    /// </summary>
    public const string FingerprintParameter = "fp";

    /// <summary>
    /// Name of the parameter that carries the address of the server (AD-27).
    /// </summary>
    public const string AddressParameter = "addr";

    /// <summary>
    /// Builds the payload that the QR carries: the fingerprint of the ancla y la dirección
    /// del servidor (AD-27).
    /// </summary>
    /// <param name="fingerprint">Fingerprint of the ancla, in lowercase hexadecimal.</param>
    /// <param name="serverAddress">Address of the server, in the form <c>host:puerto</c>.</param>
    /// <returns>The payload of the QR.</returns>
    public static string BuildPayload(string fingerprint, string serverAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);

        var builder = new StringBuilder(Scheme);

        builder.Append('?');
        builder.Append(FingerprintParameter);
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(fingerprint));

        builder.Append('&');
        builder.Append(AddressParameter);
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(serverAddress));

        return builder.ToString();
    }

    /// <summary>
    /// Generates the código QR del ancla de confianza in SVG (AD-27).
    /// </summary>
    /// <param name="fingerprint">Fingerprint of the ancla, in lowercase hexadecimal.</param>
    /// <param name="serverAddress">Address of the server, in the form <c>host:puerto</c>.</param>
    /// <returns>The SVG of the QR.</returns>
    public static string ExportSvg(string fingerprint, string serverAddress)
    {
        var payload = BuildPayload(fingerprint, serverAddress);

        using var generator = new QRCodeGenerator();

        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);

        var svg = new SvgQRCode(data);

        return svg.GetGraphic(4);
    }

    /// <summary>
    /// Generates the código QR del ancla de confianza in PNG (AD-27).
    /// </summary>
    /// <param name="fingerprint">Fingerprint of the ancla, in lowercase hexadecimal.</param>
    /// <param name="serverAddress">Address of the server, in the form <c>host:puerto</c>.</param>
    /// <param name="pixelsPerModule">Size of every module of the QR, in pixels.</param>
    /// <returns>The bytes of the PNG.</returns>
    public static byte[] ExportPng(string fingerprint, string serverAddress, int pixelsPerModule = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerModule, 1);

        var payload = BuildPayload(fingerprint, serverAddress);

        using var generator = new QRCodeGenerator();

        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);

        var png = new PngByteQRCode(data);

        return png.GetGraphic(pixelsPerModule);
    }

    /// <summary>
    /// Builds the HOJA IMPRIMIBLE del ancla de confianza: el fingerprint agrupado, la
    /// dirección del servidor y el código QR, en una página preparada para el papel
    /// (AC1, AD-27).
    ///
    /// <para>
    /// AD-27: la distribución del ancla es un requisito de OPERACIÓN. El organizador
    /// imprime la hoja en la mesa de registro y el jugador escanea el QR o teclea el
    /// fingerprint que lleva, así que la hoja tiene que llevar las dos cosas (AC1).
    /// </para>
    /// </summary>
    /// <param name="fingerprint">Fingerprint of the ancla, in lowercase hexadecimal.</param>
    /// <param name="serverAddress">Address of the server, in the form <c>host:puerto</c>.</param>
    /// <returns>The SVG of the printable sheet.</returns>
    public static string ExportPrintableSheet(string fingerprint, string serverAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);

        var qr = ExportSvg(fingerprint, serverAddress);

        // El SVG del QR lleva su propia declaración y su propio tamaño, así que se
        // incrusta dentro de la hoja como un grupo con su traslación y su escala.
        var builder = new StringBuilder();

        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<svg xmlns="http://www.w3.org/2000/svg" width="210mm" height="297mm" viewBox="0 0 210 297">""");
        builder.AppendLine("""  <rect width="210" height="297" fill="#ffffff"/>""");
        builder.AppendLine("""  <g font-family="DejaVu Sans, Segoe UI, Arial, sans-serif" fill="#000000">""");
        builder.AppendLine("""    <text x="20" y="30" font-size="9" font-weight="bold">LAN Party Hub — Ancla de confianza del evento</text>""");
        builder.AppendLine("""    <text x="20" y="44" font-size="5">Entrega este fingerprint a los jugadores por un canal que NO pase por la red del evento.</text>""");
        builder.AppendLine("""    <text x="20" y="58" font-size="7" font-weight="bold">Fingerprint</text>""");

        var grouped = TrustAnchorViewModel.Group(fingerprint);

        builder.AppendLine(
            $"    <text x=\"20\" y=\"72\" font-size=\"9\" font-family=\"DejaVu Sans Mono, Consolas, monospace\">{Escape(grouped)}</text>");

        builder.AppendLine("""    <text x="20" y="86" font-size="7" font-weight="bold">Dirección del servidor</text>""");
        builder.AppendLine(
            $"    <text x=\"20\" y=\"100\" font-size=\"9\" font-family=\"DejaVu Sans Mono, Consolas, monospace\">{Escape(serverAddress)}</text>");

        builder.AppendLine("""    <g transform="translate(20, 112) scale(0.30)">""");
        builder.AppendLine("      " + StripXmlDeclaration(qr));
        builder.AppendLine("    </g>");
        builder.AppendLine("""  </g>""");
        builder.AppendLine("""</svg>""");

        return builder.ToString();
    }

    /// <summary>
    /// Escapes the characters of a text that goes into an SVG.
    /// </summary>
    /// <param name="text">Text to escape.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text) =>
        text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// Removes the XML declaration of an SVG, because the QR goes INSIDE the sheet and no
    /// document can carry two (AD-27).
    /// </summary>
    /// <param name="svg">SVG of the QR.</param>
    /// <returns>The SVG without its declaration.</returns>
    private static string StripXmlDeclaration(string svg)
    {
        var start = svg.IndexOf("<svg", StringComparison.Ordinal);

        return start < 0 ? svg : svg[start..];
    }

    /// <summary>
    /// Name of the file that holds the printable sheet, without the extension (AD-27).
    /// </summary>
    public static string PrintableFileName =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"ancla-de-confianza-{DateTime.UtcNow:yyyyMMdd}");
}
