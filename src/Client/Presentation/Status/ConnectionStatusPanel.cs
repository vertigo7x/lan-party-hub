using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Status;

/// <summary>
/// PERSISTENT panel of the connection of the player (AC1, AC2, AC3, AC4, UX-DR-11,
/// UX-DR-23, UX-DR-29).
///
/// <para>
/// El panel es PERSISTENTE: no se colapsa, no se esconde en un menú ni en un modal, y
/// vive en una columna fija de la ventana, a la derecha del contenido (AC1).
/// </para>
/// <para>
/// El panel muestra el PUNTO DE PRESENCIA, el nombre del servidor, la dirección en uso, la
/// versión del servidor y la última operación terminada (AC1, AC2, UX-DR-11, UX-DR-23).
/// </para>
/// <para>
/// Cuando el servidor no está disponible, el punto de presencia pasa a
/// <c>state-warning</c>, aparece el aviso «Servidor no disponible. Se conserva el último
/// catálogo sincronizado.» y el reintento con su contador «Reintentando · 3 s». NO hay
/// modal que interrumpa (AC3, AC4, UX-DR-29).
/// </para>
/// <para>
/// El color del punto de presencia NUNCA sustituye al texto: el punto lleva su color
/// canónico y su texto, exactamente como la insignia del estado del videojuego (AC3, AC4).
/// </para>
/// </summary>
public sealed class ConnectionStatusPanel : UserControl
{
    private readonly ConnectionStatusPanelViewModel _viewModel;
    private readonly Border _presenceBadge;
    private readonly TextBlock _presenceText;
    private readonly TextBlock _unavailableNotice;
    private readonly TextBlock _retryCountdown;
    private readonly TextBlock _serverName;
    private readonly TextBlock _address;
    private readonly TextBlock _serverVersion;
    private readonly TextBlock _lastOperation;

    /// <summary>Creates the persistent panel of the connection.</summary>
    /// <param name="viewModel">View model of the panel.</param>
    public ConnectionStatusPanel(ConnectionStatusPanelViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        // El punto de presencia es una insignia con su color canónico y su texto: el
        // verde de la LAN cuando el servidor está disponible y el ámbar del aviso cuando
        // no lo está (AC1, AC3).
        _presenceText = new TextBlock
        {
            FontSize = DesignTokens.MetaFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _presenceBadge = new Border
        {
            CornerRadius = new CornerRadius(DesignTokens.StatusBadgeRadius),
            Padding = new Thickness(
                DesignTokens.StatusBadgePaddingHorizontal,
                2,
                DesignTokens.StatusBadgePaddingHorizontal,
                2),
            Child = _presenceText,
        };

        _unavailableNotice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Foreground = new SolidColorBrush(DesignTokens.StateWarning),
        };

        _retryCountdown = new TextBlock
        {
            IsVisible = false,
            Foreground = new SolidColorBrush(DesignTokens.StateWarning),
        };

        _serverName = new TextBlock();
        _address = new TextBlock();
        _serverVersion = new TextBlock();
        _lastOperation = new TextBlock { TextWrapping = TextWrapping.Wrap };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Conexión",
                    FontSize = DesignTokens.TitleFontSize,
                    FontWeight = FontWeight.SemiBold,
                },
                _presenceBadge,
                _unavailableNotice,
                _retryCountdown,
                BuildRow(ConnectionStatusPanelViewModel.PresenceLabelText, _presenceText),
                BuildRow(ConnectionStatusPanelViewModel.ServerNameLabelText, _serverName),
                BuildRow(ConnectionStatusPanelViewModel.AddressLabelText, _address),
                BuildRow(ConnectionStatusPanelViewModel.ServerVersionLabelText, _serverVersion),
                BuildRow(ConnectionStatusPanelViewModel.LastOperationLabelText, _lastOperation),
            },
        };

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The point of presence of the panel, so it can be inspected and driven from the
    /// outside (AC1, AC3).
    /// </summary>
    public Border PresenceBadge => _presenceBadge;

    /// <summary>
    /// The notice of the unavailable server, so it can be inspected and driven from the
    /// outside (AC3).
    /// </summary>
    public TextBlock UnavailableNotice => _unavailableNotice;

    /// <summary>
    /// The countdown of the retry, so it can be inspected and driven from the outside
    /// (AC4).
    /// </summary>
    public TextBlock RetryCountdown => _retryCountdown;

    /// <summary>
    /// The text of the last operation that finished, so it can be inspected and driven from
    /// the outside (AC2).
    /// </summary>
    public TextBlock LastOperation => _lastOperation;

    /// <summary>
    /// Builds a row of the panel: a label and its value (AC1, UX-DR-11).
    /// </summary>
    /// <param name="label">Text of the label of the row.</param>
    /// <param name="value">Control that carries the value of the row.</param>
    /// <returns>The row of the panel.</returns>
    private static StackPanel BuildRow(string label, Control value)
    {
        var valueText = value as TextBlock;

        if (valueText is not null)
        {
            valueText.Foreground = new SolidColorBrush(DesignTokens.InkPrimary);
        }

        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = DesignTokens.MetaFontSize,
                    Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
                },
                value,
            },
        };
    }

    /// <summary>
    /// Renders the state of the view model in the panel (AC1, AC2, AC3, AC4).
    /// </summary>
    private void Render()
    {
        var presenceColor = _viewModel.IsAvailable
            ? DesignTokens.StateSuccess
            : DesignTokens.StateWarning;

        _presenceBadge.Background = new SolidColorBrush(
            presenceColor,
            DesignTokens.StatusBadgeBackgroundOpacity);

        _presenceText.Foreground = new SolidColorBrush(presenceColor);
        _presenceText.Text = _viewModel.PresenceText;

        _unavailableNotice.Text = _viewModel.UnavailableNoticeText;
        _unavailableNotice.IsVisible = _viewModel.ShowsUnavailableNotice;

        _retryCountdown.Text = _viewModel.RetryCountdownText;
        _retryCountdown.IsVisible = _viewModel.ShowsRetryCountdown;

        _serverName.Text = _viewModel.ServerNameText;
        _address.Text = _viewModel.AddressText;
        _serverVersion.Text = _viewModel.ServerVersionText;
        _lastOperation.Text = _viewModel.LastOperationText;
    }
}
