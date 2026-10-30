using Avalonia.Media;

namespace LANPartyHub.Client.Presentation.Design;

/// <summary>
/// The tokens of the design that the client uses (DESIGN, AC2, AC3).
///
/// <para>
/// The tokens live in the Presentation layer of the client because they are a rule of
/// presentation and not of the model: the ficha del videojuego puts the number of the
/// size in the token of sizes, the unit in the token of metadata and the version in the
/// monospaced token, exactly as the design fixes it (AC2, UX-DR-5, UX-DR-2).
/// </para>
/// <para>
/// The badge of the view state carries ONE color with its canonical meaning: the blue of
/// the progress for «sincronizando», the green of the LAN for «disponible» and the amber of
/// the warning for «instalado con versión desactualizada». The color NEVER replaces the
/// text: the badge always shows the text of the single table of microcopy (AC3, AC4).
/// </para>
/// </summary>
public static class DesignTokens
{
    /// <summary>Family of the interface typography.</summary>
    public const string InterfaceFontFamily = "Inter";

    /// <summary>Family of the monospaced typography: versions, hashes and derived values.</summary>
    public const string MonoFontFamily = "Cascadia Mono";

    /// <summary>Size of the body text, in pixels.</summary>
    public const double BodyFontSize = 14;

    /// <summary>Size of the strong body text, in pixels.</summary>
    public const double BodyStrongFontSize = 14;

    /// <summary>Size of the title of the ficha, in pixels.</summary>
    public const double TitleFontSize = 17;

    /// <summary>Size of the metadata typography, in pixels.</summary>
    public const double MetaFontSize = 11;

    /// <summary>Size of the monospaced typography, in pixels.</summary>
    public const double MonoFontSize = 12;

    /// <summary>Size of the derived numeric value of a size, in pixels.</summary>
    public const double SizeFontSize = 12;

    /// <summary>Height of the badge of the state, in pixels.</summary>
    public const double StatusBadgeHeight = 20;

    /// <summary>Radius of the badge of the state, in pixels.</summary>
    public const double StatusBadgeRadius = 9999;

    /// <summary>Horizontal padding of the badge of the state, in pixels.</summary>
    public const double StatusBadgePaddingHorizontal = 8;

    /// <summary>Opacity of the background of the badge of the state.</summary>
    public const byte StatusBadgeBackgroundOpacity = 0x2E;

    /// <summary>Blue of the progress: the state «sincronizando» (AC3, DESIGN).</summary>
    public static readonly Color StateProgress = Color.FromRgb(0x5B, 0x8C, 0xFF);

    /// <summary>Green of the LAN: the state «disponible» (AC3, DESIGN).</summary>
    public static readonly Color StateSuccess = Color.FromRgb(0x4D, 0xD8, 0xA4);

    /// <summary>Amber of the warning: the state «instalado con versión desactualizada» (AC3, DESIGN).</summary>
    public static readonly Color StateWarning = Color.FromRgb(0xF2, 0xB4, 0x41);

    /// <summary>Ink of the primary text (DESIGN).</summary>
    public static readonly Color InkPrimary = Color.FromRgb(0xF2, 0xF4, 0xF8);

    /// <summary>Ink of the secondary text: metadata and units (DESIGN).</summary>
    public static readonly Color InkSecondary = Color.FromRgb(0xA7, 0xAE, 0xBD);

    /// <summary>Ink of the tertiary text (DESIGN).</summary>
    public static readonly Color InkTertiary = Color.FromRgb(0x6E, 0x76, 0x88);

    /// <summary>Surface of the raised cards and of the ficha (DESIGN).</summary>
    public static readonly Color SurfaceRaised = Color.FromRgb(0x1B, 0x1E, 0x26);

    /// <summary>Surface of the overlay (DESIGN).</summary>
    public static readonly Color SurfaceOverlay = Color.FromRgb(0x23, 0x27, 0x33);

    /// <summary>Border of hairline that separates the surfaces (DESIGN).</summary>
    public static readonly Color BorderHairline = Color.FromRgb(0x28, 0x2C, 0x37);

    /// <summary>
    /// Color of the badge of a derived view state (AC3).
    ///
    /// <para>
    /// The color is the one with its canonical meaning and the badge NEVER carries two of
    /// them: a state that needs two dimensions is two badges with text (DESIGN).
    /// </para>
    /// </summary>
    /// <param name="viewState">Derived view state of the videogame.</param>
    /// <returns>The color of the badge of the state.</returns>
    public static Color BadgeColorFor(Shared.Contracts.CatalogViewState viewState) =>
        viewState switch
        {
            Shared.Contracts.CatalogViewState.Synchronizing => StateProgress,
            Shared.Contracts.CatalogViewState.Available => StateSuccess,
            Shared.Contracts.CatalogViewState.InstalledOutdatedVersion => StateWarning,
            _ => throw new ArgumentOutOfRangeException(
                nameof(viewState),
                viewState,
                "Unknown catalog view state."),
        };
}
