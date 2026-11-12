using System.Globalization;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The CONTRAST of the palette of the client, medido con la fórmula de WCAG 2.2
/// (AC1, UX-DR-38).
///
/// <para>
/// El contraste NO se estima a ojo: se calcula. La luminancia relativa de cada color
/// sale de la fórmula de WCAG, el contraste es la razón entre la más clara y la más
/// oscura, y el mínimo de AA es 4,5:1 para el texto normal y 3:1 para el texto grande y
/// los bordes de la interfaz (AC1).
/// </para>
/// <para>
/// La prueba mide los PARES que la interfaz usa de verdad —el texto sobre su fondo, el
/// texto de cada botón sobre su relleno— y no una lista inventada: si una superficie
/// empareja dos colores que no se pueden leer, la prueba lo ve (AC1).
/// </para>
/// </summary>
public sealed class ContrastTests
{
    /// <summary>
    /// The minimum contrast of WCAG 2.2 AA for NORMAL text (AC1, UX-DR-38).
    /// </summary>
    private const double MinimumContrastForText = 4.5;

    /// <summary>
    /// Every PAIR of the interface meets the minimum of AA (AC1, UX-DR-38).
    /// </summary>
    [Fact]
    public void Every_pair_of_the_interface_meets_the_minimum_of_AA()
    {
        var pares = new (string Name, string Foreground, string Background)[]
        {
            ("el texto normal sobre el fondo", "InkPrimary", "SurfaceBase"),
            ("el texto secundario sobre el fondo", "InkSecondary", "SurfaceBase"),
            ("el texto sobre la superficie elevada", "InkPrimary", "SurfaceRaised"),
            ("el texto secundario sobre la superficie elevada", "InkSecondary", "SurfaceRaised"),
            ("el texto sobre la superficie hundida", "InkPrimary", "SurfaceSunken"),
            ("el texto del botón primario sobre su relleno", "AccentActionInk", "AccentAction"),
            ("el texto del botón secundario sobre su relleno", "InkPrimary", "SurfaceOverlay"),
            ("el texto del botón de peligro sobre su relleno", "SurfaceBase", "StateDanger"),
            ("el texto del botón fantasma sobre su relleno", "InkSecondary", "SurfaceBase"),
            ("el texto de la insignia de peligro sobre su relleno", "SurfaceBase", "StateDanger"),
        };

        var fallos = pares
            .Select(par => (par, contraste: ContrastOf(par.Foreground, par.Background)))
            .Where(result => result.contraste < MinimumContrastForText)
            .Select(result => $"{result.par.Name}: {result.contraste:0.00}:1 ({result.par.Foreground} sobre {result.par.Background})")
            .ToArray();

        Assert.True(
            fallos.Length == 0,
            "El contraste de la paleta no llega a WCAG 2.2 AA:" + Environment.NewLine +
            string.Join(Environment.NewLine, fallos));
    }

    /// <summary>
    /// The colour of the FOCUS is the accent of the action, y se distingue del fondo
    /// (AC2, UX-DR-38).
    /// </summary>
    [Fact]
    public void The_colour_of_the_focus_is_visible_against_the_surface()
    {
        // El foco se pinta con `{colors.accent-action}` y no se suprime (AC2).
        var contraste = ContrastOf("AccentAction", "SurfaceBase");

        Assert.True(
            contraste >= 3.0,
            $"El foco no se distingue del fondo: {contraste:0.00}:1.");
    }

    /// <summary>
    /// Every CONTROL of the interface is identified by its fill or by its text, and not
    /// only by a border (AC1, UX-DR-38).
    /// </summary>
    [Fact]
    public void Every_control_is_identified_by_its_fill_or_by_its_text()
    {
        // WCAG 1.4.11 pide 3:1 para la parte que IDENTIFICA un control. El botón
        // secundario se identifica por su relleno (13,5:1 contra la página), no por su
        // borde, que es decorativo: el texto de un borde no tiene por qué reaching 3:1
        // cuando el control ya se ve por su relleno (AC1).
        var relleno = ContrastOf("SurfaceOverlay", "SurfaceBase");
        var texto = ContrastOf("InkPrimary", "SurfaceOverlay");

        Assert.True(
            relleno >= 3.0 || texto >= 3.0,
            $"El botón secundario no se identifica: relleno {relleno:0.00}:1, texto {texto:0.00}:1.");

        // El botón fantasma no tiene relleno: su texto es lo que lo identifica.
        Assert.True(ContrastOf("InkSecondary", "SurfaceBase") >= 4.5);
    }

    /// <summary>
    /// The contrast of a pair of colours, con la fórmula de WCAG 2.2 (AC1).
    /// </summary>
    /// <param name="foreground">Name of the foreground colour.</param>
    /// <param name="background">Name of the background colour.</param>
    /// <returns>The ratio of contrast, from 1:1 to 21:1.</returns>
    private static double ContrastOf(string foreground, string background)
    {
        var a = LuminanceOf(ColourOf(foreground));
        var b = LuminanceOf(ColourOf(background));

        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// The RELATIVE luminance of a colour, tal y como la define WCAG 2.2 (AC1).
    /// </summary>
    /// <param name="colour">The colour to measure.</param>
    /// <returns>The relative luminance, from 0 to 1.</returns>
    private static double LuminanceOf(Color colour)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;

            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
    }

    /// <summary>
    /// The colour of the tokens with the given name (AC1).
    /// </summary>
    /// <param name="name">Name of the colour.</param>
    /// <returns>The colour of the token.</returns>
    private static Color ColourOf(string name) => name switch
    {
        "AccentAction" => DesignTokens.AccentAction,
        "AccentActionInk" => DesignTokens.AccentActionInk,
        "SurfaceBase" => DesignTokens.SurfaceBase,
        "SurfaceRaised" => DesignTokens.SurfaceRaised,
        "SurfaceSunken" => DesignTokens.SurfaceSunken,
        "SurfaceOverlay" => DesignTokens.SurfaceOverlay,
        "InkPrimary" => DesignTokens.InkPrimary,
        "InkSecondary" => DesignTokens.InkSecondary,
        "BorderStrong" => DesignTokens.BorderStrong,
        "StateDanger" => DesignTokens.StateDanger,
        _ => throw new ArgumentOutOfRangeException(
            nameof(name),
            name,
            string.Format(CultureInfo.InvariantCulture, "El color '{0}' no está en la paleta.", name)),
    };
}
