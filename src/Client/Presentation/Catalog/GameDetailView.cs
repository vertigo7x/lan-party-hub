using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.GameLauncher;

namespace LANPartyHub.Client.Presentation.Catalog;

/// <summary>
/// The ficha del videojuego (AC1, AC2, AC3, AC4, FR-2, UX-DR-2, UX-DR-5, UX-DR-27, AR-8).
///
/// <para>
/// The ficha shows the EXACT values of the videogame: the version in the monospaced
/// token, the numeric value of the size in the token of sizes with its unit in the token
/// of metadata, the number of players, the operating system, the requirements and the
/// license (AC2, UX-DR-5, UX-DR-2).
/// </para>
/// <para>
/// The badge of the view state carries the color with its canonical meaning AND the text
/// of the single table of microcopy: the color is never the only signal (AC3, AC4, AR-8).
/// </para>
/// <para>
/// The view does NOT define a single text of state: it only paints what the view model
/// resolved (AC4, R-MVP-7).
/// </para>
/// </summary>
public sealed class GameDetailView : UserControl
{
    private GameDetailViewModel? _viewModel;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly TextBlock _version;
    private readonly TextBlock _sizeValue;
    private readonly TextBlock _sizeUnit;
    private readonly TextBlock _players;
    private readonly TextBlock _operatingSystem;
    private readonly TextBlock _requirements;
    private readonly TextBlock _license;
    private readonly TextBlock _availableVersion;
    private readonly TextBlock _installedVersion;
    private readonly GamePlayActionsView _playActions;
    private readonly TextBlock _launchFailure;

    /// <summary>
    /// Creates the ficha del videojuego.
    /// </summary>
    /// <param name="viewModel">View model of the ficha.</param>
    /// <param name="onPlay">
    /// Action that the player triggers with «Jugar», or <c>null</c> when the ficha is
    /// built without it (AC1).
    /// </param>
    /// <param name="onPrimary">
    /// Action of the PRIMARY button of the table of actions, or <c>null</c> when the
    /// ficha is built without it (AC1, AC2).
    ///
    /// <para>
    /// La primaria es POLIMÓRFICA: es «Reparar» o «Descargar de nuevo» según la
    /// decisión, así que la acción recibe ALSO WHICH primary action was pressed, so the
    /// wiring registers the RIGHT intention (AC1, AC2).
    /// </para>
    /// </param>
    /// <param name="onSecondary">
    /// Action of the SECONDARY button of the table of actions, or <c>null</c> when the
    /// ficha is built without it (AC1, AC2).
    /// </param>
    public GameDetailView(
        GameDetailViewModel viewModel,
        Action<string>? onPlay = null,
        Action<string, PlayabilityAction>? onPrimary = null,
        Action<string>? onSecondary = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _title = new TextBlock
        {
            FontSize = DesignTokens.TitleFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _description = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = DesignTokens.BodyFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
        };

        _badgeText = new TextBlock
        {
            FontSize = DesignTokens.MetaFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _badge = new Border
        {
            Height = DesignTokens.StatusBadgeHeight,
            CornerRadius = new CornerRadius(DesignTokens.StatusBadgeRadius),
            Padding = new Thickness(DesignTokens.StatusBadgePaddingHorizontal, 0, DesignTokens.StatusBadgePaddingHorizontal, 0),
            Child = _badgeText,
        };

        _version = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.MonoFontFamily),
            FontSize = DesignTokens.MonoFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _sizeValue = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.MonoFontFamily),
            FontSize = DesignTokens.SizeFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _sizeUnit = new TextBlock
        {
            FontSize = DesignTokens.MetaFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
        };

        _players = new TextBlock
        {
            FontSize = DesignTokens.BodyStrongFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _operatingSystem = new TextBlock
        {
            FontSize = DesignTokens.BodyStrongFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _requirements = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = DesignTokens.BodyFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _license = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = DesignTokens.BodyFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _installedVersion = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.MonoFontFamily),
            FontSize = DesignTokens.MonoFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
        };

        // AC1, AC3: la línea de la versión PUBLICADA lleva su etiqueta y su valor EXACTO en
        // la tipografía monoespaciada, así que el jugador compara las dos versiones con los
        // dígitos alineados (AC3, UX-DR-21).
        _availableVersion = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.MonoFontFamily),
            FontSize = DesignTokens.MonoFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        // AC1, AC2: la ficha lleva la MISMA tabla de acciones que la Biblioteca, con el
        // motivo VISIBLE del bloqueo junto a la etiqueta de «Jugar» (AC3, UX-DR-18).
        _playActions = new GamePlayActionsView(viewModel.Actions, onPlay, onPrimary, onSecondary);

        _launchFailure = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(DesignTokens.StateDanger),
            IsVisible = false,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(DesignTokens.Spacing4),
            Spacing = DesignTokens.Spacing3,
            Children =
            {
                _title,
                _description,
                _badge,
                BuildFact("Versión", _version),
                BuildSizeFact(),
                BuildFact("Jugadores", _players),
                BuildFact("Sistema operativo", _operatingSystem),
                BuildFact("Requisitos", _requirements),
                BuildFact("Licencia", _license),

                // AC1, AC3: la versión PUBLICADA y la INSTALADA van en la tipografía
                // monoespaciada, con su valor EXACTO, así que el jugador compara los
                // dígitos alineados (AC3, UX-DR-21).
                BuildFact(GameDetailViewModel.AvailableVersionLabel, _availableVersion),
                _installedVersion,
                _playActions,
                _launchFailure,
            },
        };

        Subscribe(viewModel);

        Render();
    }

    /// <summary>
    /// Repaints the ficha with a new state of the view model, when the game that is open
    /// is the same one but its derived state changed (AC2, AC3).
    ///
    /// <para>
    /// The control de la ficha NO se reconstruye: el jugador no pierde el foco ni la
    /// posición de la lectura, y solo cambian los valores que el nuevo estado lleva
    /// (AC1).
    /// </para>
    /// </summary>
    /// <param name="viewModel">View model of the ficha.</param>
    public void Update(GameDetailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Subscribe(viewModel);

        Render();
    }

    /// <summary>
    /// Subscribes the ficha to the view model that it paints, leaving the previous one:
    /// el view model anterior no puede seguir pintando una ficha que ya no es suya
    /// (AC1).
    /// </summary>
    /// <param name="viewModel">View model of the ficha.</param>
    private void Subscribe(GameDetailViewModel viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.Changed -= Render;
        }

        _viewModel = viewModel;

        _viewModel.Changed += Render;
    }

    /// <summary>
    /// Builds a fact of the ficha: its label and its value (AC2).
    /// </summary>
    /// <param name="label">Label of the fact.</param>
    /// <param name="value">Control that shows the value of the fact.</param>
    /// <returns>The control of the fact.</returns>
    private static Control BuildFact(string label, Control value) => new StackPanel
    {
        Spacing = DesignTokens.Spacing1,
        Children =
        {
            new TextBlock
            {
                Text = label,
                FontSize = DesignTokens.MetaFontSize,
                Foreground = new SolidColorBrush(DesignTokens.InkTertiary),
            },
            value,
        },
    };

    /// <summary>
    /// Builds the fact of the size: the numeric value in the token of sizes and the unit in
    /// the token of metadata (AC2, UX-DR-5).
    /// </summary>
    /// <returns>The control of the fact of the size.</returns>
    private Control BuildSizeFact() => new StackPanel
    {
        Spacing = DesignTokens.Spacing1,
        Children =
        {
            new TextBlock
            {
                Text = "Tamaño",
                FontSize = DesignTokens.MetaFontSize,
                Foreground = new SolidColorBrush(DesignTokens.InkTertiary),
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = DesignTokens.Spacing1,
                Children =
                {
                    _sizeValue,
                    _sizeUnit,
                },
            },
        },
    };

    /// <summary>
    /// Renders the state of the view model in the ficha (AC2, AC3, AC4).
    /// </summary>
    private void Render()
    {
        var viewModel = _viewModel;

        if (viewModel is null)
        {
            return;
        }

        _title.Text = viewModel.Name;
        _description.Text = viewModel.Description;

        // El texto de la insignia lo pone el view model y sale de la tabla de microcopy: la
        // vista no define ningún texto de estado (AC4, AR-8).
        _badgeText.Text = viewModel.ViewStateText;

        var badgeColor = DesignTokens.BadgeColorFor(viewModel.ViewState);

        _badge.Background = new SolidColorBrush(badgeColor, DesignTokens.StatusBadgeBackgroundOpacity);
        _badgeText.Foreground = new SolidColorBrush(badgeColor);

        _version.Text = viewModel.Version;

        // AC1, AC3: la versión PUBLICADA lleva su etiqueta y su valor EXACTO en la
        // tipografía monoespaciada (AC3, UX-DR-21).
        _availableVersion.Text = viewModel.AvailableVersionLineText;

        _sizeValue.Text = viewModel.SizeValue;
        _sizeUnit.Text = viewModel.SizeUnit;

        _players.Text = viewModel.PlayersText;
        _operatingSystem.Text = viewModel.OperatingSystem;
        _requirements.Text = viewModel.RequirementsText;
        _license.Text = viewModel.License;

        // AC1, AC3: la versión INSTALADA lleva su valor EXACTO en la tipografía
        // monoespaciada (AC3, UX-DR-21).
        _installedVersion.Text = viewModel.InstalledVersionText ?? string.Empty;

        _installedVersion.IsVisible = viewModel.InstalledVersionText is not null;

        // AC1, AC3: la tabla de acciones se repinta con la decisión vigente, y el aviso
        // del error de lanzamiento aparece JUNTO a ella (AC1, UX-DR-36).
        _playActions.Actions.Apply(viewModel.Actions.Decision);

        var launchFailureText = viewModel.LaunchFailureText;

        _launchFailure.Text = launchFailureText ?? string.Empty;

        _launchFailure.IsVisible = launchFailureText is not null;
    }
}
