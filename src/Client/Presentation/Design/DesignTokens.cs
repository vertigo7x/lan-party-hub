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

    /// <summary>Surface SUNKEN: the deepest level of the tonal depth (DESIGN, UX-DR-3).</summary>
    public static readonly Color SurfaceSunken = Color.FromRgb(0x0D, 0x0F, 0x13);

    /// <summary>Ink of the disabled state (DESIGN).</summary>
    public static readonly Color InkDisabled = Color.FromRgb(0x4A, 0x50, 0x60);

    /// <summary>
    /// Green of the LAN: the accent of a HEALTHY system, which is not the same token as
    /// the green of the success state although today they share the value (DESIGN,
    /// UX-DR-1).
    /// </summary>
    public static readonly Color AccentLan = Color.FromRgb(0x4D, 0xD8, 0xA4);

    /// <summary>Ink that goes ON the green of the LAN (DESIGN).</summary>
    public static readonly Color AccentLanInk = Color.FromRgb(0x06, 0x23, 0x1A);

    /// <summary>Ink that goes ON the blue of the action (DESIGN).</summary>
    public static readonly Color AccentActionInk = Color.FromRgb(0x05, 0x0D, 0x24);

    /// <summary>Strong border: the one that delimits a control against its surface (DESIGN).</summary>
    public static readonly Color BorderStrong = Color.FromRgb(0x3A, 0x3F, 0x4D);

    /// <summary>
    /// Variante CLARA del verde de la LAN, para las pocas superficies que la necesitan
    /// (DESIGN, UX-DR-41).
    /// </summary>
    public static readonly Color AccentLanLight = Color.FromRgb(0x12, 0x7A, 0x57);

    /// <summary>Radius of a SMALL text field or of a label, in pixels (DESIGN, UX-DR-4).</summary>
    public const double RadiusSmall = 4;

    /// <summary>Radius of a CONTROL, in pixels (DESIGN, UX-DR-4).</summary>
    public const double RadiusControl = 8;

    /// <summary>Radius of a CARD or of a PANEL, in pixels (DESIGN, UX-DR-4).</summary>
    public const double RadiusCard = 12;

    /// <summary>Radius of a DIALOG, in pixels (DESIGN, UX-DR-4).</summary>
    public const double RadiusDialog = 16;

    /// <summary>Radius of a PILL: the only shape that is round (DESIGN, UX-DR-4).</summary>
    public const double RadiusFull = 9999;

    /// <summary>Spacing scale 1, in pixels: 4 (DESIGN).</summary>
    public const double Spacing1 = 4;

    /// <summary>Spacing scale 2, in pixels: 8 (DESIGN).</summary>
    public const double Spacing2 = 8;

    /// <summary>Spacing scale 3, in pixels: 12 (DESIGN).</summary>
    public const double Spacing3 = 12;

    /// <summary>Spacing scale 4, in pixels: 16 (DESIGN).</summary>
    public const double Spacing4 = 16;

    /// <summary>Spacing scale 5, in pixels: 24 (DESIGN).</summary>
    public const double Spacing5 = 24;

    /// <summary>Spacing scale 6, in pixels: 32 (DESIGN).</summary>
    public const double Spacing6 = 32;

    /// <summary>Spacing scale 7, in pixels: 48 (DESIGN).</summary>
    public const double Spacing7 = 48;

    /// <summary>Separator BETWEEN columns, in pixels (DESIGN, UX-DR-4).</summary>
    public const double SeparatorGutter = 24;

    /// <summary>Separator INSIDE a panel, in pixels (DESIGN, UX-DR-4).</summary>
    public const double SeparatorPanel = 16;

    /// <summary>Separator INSIDE a card or a dialog, in pixels (DESIGN, UX-DR-4).</summary>
    public const double SeparatorCard = 20;

    /// <summary>
    /// The SEVEN weights that Inter declares, and the role of each one in the interface
    /// (DESIGN, UX-DR-2).
    ///
    /// <para>
    /// La tipografia de la interfaz son siete estilos de Inter, cada uno con su peso: el
    /// <c>display</c>, el <c>headline</c>, el <c>title</c>, el <c>body</c>, el
    /// <c>body-strong</c>, el <c>label</c> y el <c>meta</c>. El peso vive en el token y
    /// no escrito a mano en el componente: un peso escrito a mano es un token fuera del
    /// diseno (AC1).
    /// </para>
    /// </summary>
    public enum TextStyle
    {
        /// <summary>The display typography: the greatest size of the interface.</summary>
        Display,

        /// <summary>The headline typography: the title of a surface.</summary>
        Headline,

        /// <summary>The title typography: the title of an item.</summary>
        Title,

        /// <summary>The body typography: the text that the player reads.</summary>
        Body,

        /// <summary>The strong body typography: the text that the player must notice.</summary>
        BodyStrong,

        /// <summary>The label typography: the label of a control.</summary>
        Label,

        /// <summary>The meta typography: the unit and the secondary metadata.</summary>
        Meta,
    }

    /// <summary>Weight of the DISPLAY typography (DESIGN).</summary>
    public static readonly FontWeight DisplayFontWeight = FontWeight.Bold;

    /// <summary>Weight of the HEADLINE typography (DESIGN).</summary>
    public static readonly FontWeight HeadlineFontWeight = FontWeight.SemiBold;

    /// <summary>Weight of the TITLE typography (DESIGN).</summary>
    public static readonly FontWeight TitleFontWeight = FontWeight.SemiBold;

    /// <summary>Weight of the BODY typography (DESIGN).</summary>
    public static readonly FontWeight BodyFontWeight = FontWeight.Normal;

    /// <summary>Weight of the STRONG BODY typography (DESIGN).</summary>
    public static readonly FontWeight BodyStrongFontWeight = FontWeight.SemiBold;

    /// <summary>Weight of the LABEL typography (DESIGN).</summary>
    public static readonly FontWeight LabelFontWeight = FontWeight.Medium;

    /// <summary>Weight of the META typography (DESIGN).</summary>
    public static readonly FontWeight MetaFontWeight = FontWeight.Medium;

    /// <summary>Weight of the MONOSPACED typography (DESIGN).</summary>
    public static readonly FontWeight MonoFontWeight = FontWeight.Normal;

    /// <summary>
    /// The size of a text style, in pixels (DESIGN, UX-DR-2).
    /// </summary>
    /// <param name="style">Style of the text.</param>
    /// <returns>The size of that style, in pixels.</returns>
    public static double FontSizeOf(TextStyle style) => style switch
    {
        TextStyle.Display => DisplayFontSize,
        TextStyle.Headline => HeadlineFontSize,
        TextStyle.Title => TitleFontSize,
        TextStyle.Body => BodyFontSize,
        TextStyle.BodyStrong => BodyStrongFontSize,
        TextStyle.Label => LabelFontSize,
        TextStyle.Meta => MetaFontSize,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown text style."),
    };

    /// <summary>
    /// The weight of a text style (DESIGN, UX-DR-2).
    /// </summary>
    /// <param name="style">Style of the text.</param>
    /// <returns>The weight of that style.</returns>
    public static FontWeight FontWeightOf(TextStyle style) => style switch
    {
        TextStyle.Display => DisplayFontWeight,
        TextStyle.Headline => HeadlineFontWeight,
        TextStyle.Title => TitleFontWeight,
        TextStyle.Body => BodyFontWeight,
        TextStyle.BodyStrong => BodyStrongFontWeight,
        TextStyle.Label => LabelFontWeight,
        TextStyle.Meta => MetaFontWeight,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown text style."),
    };

    /// <summary>
    /// Applies the typography of a text style to a block of text: its family, its size
    /// and its weight (DESIGN, UX-DR-2).
    /// </summary>
    /// <param name="text">Block of text to style.</param>
    /// <param name="style">Style of the text.</param>
    public static void Apply(Avalonia.Controls.TextBlock text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        text.FontFamily = new FontFamily(InterfaceFontFamily);
        text.FontSize = FontSizeOf(style);
        text.FontWeight = FontWeightOf(style);
    }

    /// <summary>
    /// Applies the MONOSPACED typography to a block of text: the one that carries a
    /// literal that has to align in a column (DESIGN, UX-DR-2).
    /// </summary>
    /// <param name="text">Block of text to style.</param>
    public static void ApplyMono(Avalonia.Controls.TextBlock text)
    {
        ArgumentNullException.ThrowIfNull(text);

        text.FontFamily = new FontFamily(MonoFontFamily);
        text.FontSize = MonoFontSize;
        text.FontWeight = MonoFontWeight;
    }

    /// <summary>
    /// The weight of the MONOSPACED typography, and the seven weights of Inter, live
    /// here and not written in the components (DESIGN, UX-DR-1, AC1).
    /// </summary>
    /// <summary>Size of the DISPLAY typography, in pixels (DESIGN).</summary>
    public const double DisplayFontSize = 32;

    /// <summary>Size of the HEADLINE typography, in pixels (DESIGN).</summary>
    public const double HeadlineFontSize = 22;

    /// <summary>Size of the LABEL typography, in pixels (DESIGN).</summary>
    public const double LabelFontSize = 12;

    /// <summary>
    /// Opacity of the shadow that the ONLY shadow admits: the one of the modal dialog and
    /// of the drop-down menu, over the sunken surface (DESIGN, UX-DR-3).
    /// </summary>
    public const byte OverlayShadowOpacity = 0x66;

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
