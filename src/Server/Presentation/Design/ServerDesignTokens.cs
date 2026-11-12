
using LANPartyHub.Server.Presentation.Design;using Avalonia.Media;

namespace LANPartyHub.Server.Presentation.Design;

/// <summary>
/// The TOKENS of the design for the ORGANIZER (AD-1, DESIGN, UX-DR-1).
///
/// <para>
/// El servidor es un desplegable APARTE, así que no puede tomar los tokens del cliente:
/// esta clase lleva los MISMOS valores que el <c>DESIGN.md</c> manda, y el guard de
/// contraste comprueba los dos lados contra la misma norma (AC1, UX-DR-38).
/// </para>
/// </summary>
public static class ServerDesignTokens
{
    /// <summary>The base surface of the interface (DESIGN).</summary>
    public static readonly Color SurfaceBase = Color.FromRgb(0x12, 0x14, 0x1A);

    /// <summary>The raised surface of the interface (DESIGN).</summary>
    public static readonly Color SurfaceRaised = Color.FromRgb(0x1B, 0x1E, 0x26);

    /// <summary>The overlay surface of the interface (DESIGN).</summary>
    public static readonly Color SurfaceOverlay = Color.FromRgb(0x23, 0x27, 0x33);

    /// <summary>The primary ink of the interface (DESIGN).</summary>
    public static readonly Color InkPrimary = Color.FromRgb(0xF2, 0xF4, 0xF8);

    /// <summary>The secondary ink of the interface (DESIGN).</summary>
    public static readonly Color InkSecondary = Color.FromRgb(0xA7, 0xAE, 0xBD);

    /// <summary>The accent of the actionable things (DESIGN).</summary>
    public static readonly Color AccentAction = Color.FromRgb(0x5B, 0x8C, 0xFF);

    /// <summary>The ink that goes on the accent (DESIGN).</summary>
    public static readonly Color AccentActionInk = Color.FromRgb(0x05, 0x0D, 0x24);

    /// <summary>The colour of the danger (DESIGN).</summary>
    public static readonly Color StateDanger = Color.FromRgb(0xF2, 0x54, 0x5B);

    /// <summary>The hairline that separates the surfaces (DESIGN).</summary>
    public static readonly Color BorderHairline = Color.FromRgb(0x28, 0x2C, 0x37);

    /// <summary>Height of a row of the dense table, in pixels (UX-DR-14).</summary>
    public const double RowHeight = 40;

    /// <summary>Height of a button, in pixels (UX-DR-7).</summary>
    public const double ButtonHeight = 36;
}
