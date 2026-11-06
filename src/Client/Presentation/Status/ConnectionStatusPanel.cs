using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Client.Presentation.Install;

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
    private readonly GameRunStatusViewModel? _gameRunStatusViewModel;
    private readonly Border _presenceBadge;
    private readonly TextBlock _presenceText;
    private readonly TextBlock _presenceValue;
    private readonly TextBlock _unavailableNotice;
    private readonly TextBlock _retryCountdown;
    private readonly TextBlock _serverName;
    private readonly TextBlock _address;
    private readonly TextBlock _serverVersion;
    private readonly TextBlock _lastOperation;
    private readonly TextBlock _gameRunStatus;

    /// <summary>
    /// Creates the persistent panel of the connection.
    /// </summary>
    /// <param name="viewModel">View model of the panel.</param>
    /// <param name="downloadProgress">
    /// Control of the progress of the download in progress, or <c>null</c> when the panel
    /// is built without it. UX-DR-11: el panel muestra abajo la operación en curso con su
    /// barra.
    /// </param>
    /// <param name="installProgress">
    /// Control of the progress of the installation in progress, or <c>null</c> when the
    /// panel is built without it. AC4: el panel muestra la fase y el porcentaje de la
    /// instalación con su barra.
    /// </param>
    /// <param name="gameRunStatus">
    /// View model of the state of the game that is running, or <c>null</c> when the panel
    /// is built without it. AC2: el panel muestra «En ejecución» o «Ningún juego en
    /// ejecución».
    /// </param>
    public ConnectionStatusPanel(
        ConnectionStatusPanelViewModel viewModel,
        DownloadProgressControl? downloadProgress = null,
        InstallProgressControl? installProgress = null,
        GameRunStatusViewModel? gameRunStatus = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        _gameRunStatusViewModel = gameRunStatus;

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

        // AC2, UX-DR-6: la fila del estado de ejecución dice «En ejecución» o «Ningún
        // juego en ejecución». El panel NUNCA deja un espacio vacío (AC2).
        _gameRunStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };

        // La fila de la presencia lleva SU PROPIO texto: el texto de la insignia ya es su
        // hijo visual, y un control de Avalonia NO puede estar en dos sitios a la vez
        // (AC1).
        _presenceValue = new TextBlock();

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
                BuildRow(ConnectionStatusPanelViewModel.PresenceLabelText, _presenceValue),
                BuildRow(ConnectionStatusPanelViewModel.ServerNameLabelText, _serverName),
                BuildRow(ConnectionStatusPanelViewModel.AddressLabelText, _address),
                BuildRow(ConnectionStatusPanelViewModel.ServerVersionLabelText, _serverVersion),
                BuildRow(ConnectionStatusPanelViewModel.LastOperationLabelText, _lastOperation),
            },
        };

        // AC2, UX-DR-6: la fila del estado de ejecución SÓLO existe cuando el panel
        // conoce el view model del juego en ejecución, así que el panel no inventa el
        // texto (AC4, R-MVP-7).
        if (gameRunStatus is not null)
        {
            ((StackPanel)Content).Children.Add(
                BuildRow(GameRunStatusViewModel.GameRunLabelText, _gameRunStatus));

            gameRunStatus.Changed += Render;
        }

        // UX-DR-11: el panel muestra ABAJO la operación en curso con su barra, y su
        // progreso. La operación en curso se dibuja con el MISMO view model que la barra
        // y el texto, así que no pueden contradecirse (UX-DR-5, UX-DR-13).
        if (downloadProgress is not null)
        {
            ((StackPanel)Content).Children.Add(downloadProgress);
        }

        // AC4, UX-DR-34: la instalación en curso vive en el MISMO panel persistente, con
        // su fase («Extrayendo», «Aplicando manifiesto») y su porcentaje entero de los
        // bytes REALES.
        if (installProgress is not null)
        {
            ((StackPanel)Content).Children.Add(installProgress);
        }

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
    /// The text of the state of the game that is running, so it can be inspected and
    /// driven from the outside (AC2, UX-DR-6).
    /// </summary>
    public TextBlock GameRunStatus => _gameRunStatus;

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

        // La fila de la presencia lleva el mismo texto que la insignia, en SU propio
        // control (AC1).
        _presenceValue.Text = _viewModel.PresenceText;

        _unavailableNotice.Text = _viewModel.UnavailableNoticeText;
        _unavailableNotice.IsVisible = _viewModel.ShowsUnavailableNotice;

        _retryCountdown.Text = _viewModel.RetryCountdownText;
        _retryCountdown.IsVisible = _viewModel.ShowsRetryCountdown;

        _serverName.Text = _viewModel.ServerNameText;
        _address.Text = _viewModel.AddressText;
        _serverVersion.Text = _viewModel.ServerVersionText;
        _lastOperation.Text = _viewModel.LastOperationText;

        // AC2, UX-DR-6: la fila del estado de ejecución muestra «En ejecución» o «Ningún
        // juego en ejecución», y NUNCA un espacio vacío (AC2).
        _gameRunStatus.Text = _gameRunStatusViewModel?.StatusText ?? GameRunStatusViewModel.NoGameRunningText;
    }
}
