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

    /// <summary>
    /// Height of the field of text of the interface, in pixels. The height is FIXED so the
    /// focus does not deform the grid (AC2, UX-DR-9).
    /// </summary>
    public const double FieldInputHeight = 36;

    /// <summary>
    /// Thickness of the border of the focused field, in pixels. The focus does NOT change
    /// the height of the field: the border grows inside the fixed height (AC2, UX-DR-9).
    /// </summary>
    public const double FieldInputFocusBorderThickness = 2;

    /// <summary>Radius of the badge of the state, in pixels.</summary>
    public const double StatusBadgeRadius = 9999;

    /// <summary>Horizontal padding of the badge of the state, in pixels.</summary>
    public const double StatusBadgePaddingHorizontal = 8;

    /// <summary>Opacity of the background of the badge of the state.</summary>
    public const byte StatusBadgeBackgroundOpacity = 0x2E;

    /// <summary>Blue of the progress: the state «sincronizando» (AC3, DESIGN).</summary>
    public static readonly Color StateProgress = Color.FromRgb(0x5B, 0x8C, 0xFF);

    /// <summary>
    /// Blue of the action: the primary action and the focus of the focused field (AC2,
    /// DESIGN).
    /// </summary>
    public static readonly Color AccentAction = Color.FromRgb(0x5B, 0x8C, 0xFF);

    /// <summary>Green of the LAN: the state «disponible» (AC3, DESIGN).</summary>
    public static readonly Color StateSuccess = Color.FromRgb(0x4D, 0xD8, 0xA4);

    /// <summary>Amber of the warning: the state «instalado con versión desactualizada» (AC3, DESIGN).</summary>
    public static readonly Color StateWarning = Color.FromRgb(0xF2, 0xB4, 0x41);

    /// <summary>
    /// Red of the danger: the state of a failed validation and the surface of the error
    /// notice (AC2, DESIGN, UX-DR-33).
    ///
    /// <para>
    /// El color de peligro NUNCA sustituye al texto: el estado «dañado» y el estado «no
    /// confiable» son DOS textos distintos y los dos se leen (AC1, AC2).
    /// </para>
    /// </summary>
    public static readonly Color StateDanger = Color.FromRgb(0xF2, 0x54, 0x5B);

    /// <summary>Ink of the primary text (DESIGN).</summary>
    public static readonly Color InkPrimary = Color.FromRgb(0xF2, 0xF4, 0xF8);

    /// <summary>Ink of the secondary text: metadata and units (DESIGN).</summary>
    public static readonly Color InkSecondary = Color.FromRgb(0xA7, 0xAE, 0xBD);

    /// <summary>Ink of the tertiary text (DESIGN).</summary>
    public static readonly Color InkTertiary = Color.FromRgb(0x6E, 0x76, 0x88);

    /// <summary>Surface of the raised cards and of the ficha (DESIGN).</summary>
    public static readonly Color SurfaceRaised = Color.FromRgb(0x1B, 0x1E, 0x26);

    /// <summary>
    /// Surface of the base of the window and ink of the notice of error, que va sobre la
    /// superficie del peligro (DESIGN).
    /// </summary>
    public static readonly Color SurfaceBase = Color.FromRgb(0x12, 0x14, 0x1A);

    /// <summary>Surface of the overlay (DESIGN).</summary>
    public static readonly Color SurfaceOverlay = Color.FromRgb(0x23, 0x27, 0x33);

    /// <summary>Border of hairline that separates the surfaces (DESIGN).</summary>
    public static readonly Color BorderHairline = Color.FromRgb(0x28, 0x2C, 0x37);

    /// <summary>
    /// Variante CLARA del fondo de la ventana, para las pocas superficies que la
    /// necesitan —el visor de logs y el detalle de un juego en modo claro— (DESIGN,
    /// UX-DR-41).
    /// </summary>
    public static readonly Color SurfaceBaseLight = Color.FromRgb(0xF7, 0xF8, 0xFA);

    /// <summary>
    /// Variante CLARA de la superficie elevada, para las pocas superficies que la
    /// necesitan (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color SurfaceRaisedLight = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>
    /// Variante CLARA de la tinta principal, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color InkPrimaryLight = Color.FromRgb(0x14, 0x16, 0x1C);

    /// <summary>
    /// Variante CLARA de la tinta secundaria, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color InkSecondaryLight = Color.FromRgb(0x5A, 0x61, 0x72);

    /// <summary>
    /// Variante CLARA del verde de la LAN, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color StateSuccessLight = Color.FromRgb(0x12, 0x7A, 0x57);

    /// <summary>
    /// Variante CLARA del azul de la acción, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color AccentActionLight = Color.FromRgb(0x2F, 0x5F, 0xE0);

    /// <summary>
    /// Variante CLARA del borde de hairline, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color BorderHairlineLight = Color.FromRgb(0xE2, 0xE5, 0xEB);

    /// <summary>
    /// Variante CLARA del ámbar de la advertencia, para las pocas superficies que la
    /// necesitan (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color StateWarningLight = Color.FromRgb(0x8A, 0x5A, 0x00);

    /// <summary>
    /// Variante CLARA del rojo de peligro, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color StateDangerLight = Color.FromRgb(0xC0, 0x26, 0x2E);

    /// <summary>
    /// Whether the DEFAULT theme of the client is the DARK one (DESIGN, UX-DR-1, UX-DR-41).
    ///
    /// <para>
    /// El modo oscuro es el PREDETERMINADO del cliente: la variante clara existe para las
    /// pocas superficies que la necesitan, así que no se aplica por defecto a ninguna
    /// (UX-DR-41).
    /// </para>
    /// </summary>
    public const bool IsDarkThemeTheDefault = true;

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
