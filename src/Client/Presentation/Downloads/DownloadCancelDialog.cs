using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Downloads;

/// <summary>
/// Overlay de confirmación de la cancelación de una descarga (AC4, UX-DR-8, UX-DR-16,
/// UX-DR-22).
///
/// <para>
/// El overlay es un <c>dialog-surface</c> de UN SOLO NIVEL que vive DENTRO de la ventana
/// del cliente: no se abre una ventana nueva, porque un diálogo nunca se apila sobre otro
/// y el cliente es una aplicación de escritorio con una sola ventana (UX-DR-16,
/// UX-DR-23).
/// </para>
/// <para>
/// El cuerpo NOMBRA lo que se pierde: «Se borrarán los 3,2 GB descargados». Cancelar una
/// descarga destruye trabajo no recuperable, así que la interfaz pregunta antes y la
/// acción que confirma usa la acción <c>button-danger</c> (UX-DR-8, UX-DR-22).
/// </para>
/// </summary>
public sealed class DownloadCancelDialog : UserControl
{
    /// <summary>Text of the action that confirms the cancellation (UX-DR-22).</summary>
    public const string ConfirmActionText = "Cancelar la descarga";

    /// <summary>Text of the action that keeps the download (UX-DR-16).</summary>
    public const string KeepActionText = "Seguir descargando";

    private readonly DownloadProgressViewModel _viewModel;
    private readonly Border _surface;
    private readonly TextBlock _message;
    private readonly Button _confirmButton;
    private readonly Button _keepButton;

    /// <summary>Creates the overlay de confirmación de la cancelación.</summary>
    /// <param name="viewModel">View model of the progress, whose text names what is lost.</param>
    public DownloadCancelDialog(DownloadProgressViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        _message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };

        // UX-DR-8: la acción que confirma la destrucción es la acción PELIGRO.
        _confirmButton = new Button
        {
            Content = ConfirmActionText,
            Height = DesignTokens.FieldInputHeight,
            Foreground = new SolidColorBrush(DesignTokens.StateWarning),
        };

        _keepButton = new Button
        {
            Content = KeepActionText,
            Height = DesignTokens.FieldInputHeight,
            Foreground = new SolidColorBrush(DesignTokens.InkPrimary),
        };

        _surface = new Border
        {
            Background = new SolidColorBrush(DesignTokens.SurfaceOverlay),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Width = 420,
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Cancelar la descarga",
                        FontSize = DesignTokens.TitleFontSize,
                        FontWeight = FontWeight.SemiBold,
                    },
                    _message,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            _keepButton,
                            _confirmButton,
                        },
                    },
                },
            },
        };

        Content = new Grid
        {
            Children =
            {
                new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
                },
                _surface,
            },
        };

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The surface of the dialog, so it can be inspected and driven from the outside
    /// (UX-DR-16).
    /// </summary>
    public Border Surface => _surface;

    /// <summary>
    /// The message that names what the cancellation loses, so it can be inspected and
    /// driven from the outside (UX-DR-22).
    /// </summary>
    public TextBlock Message => _message;

    /// <summary>
    /// The action that confirms the cancellation, so it can be inspected and driven from
    /// the outside (UX-DR-8).
    /// </summary>
    public Button ConfirmButton => _confirmButton;

    /// <summary>
    /// The action that keeps the download, so it can be inspected and driven from the
    /// outside (UX-DR-16).
    /// </summary>
    public Button KeepButton => _keepButton;

    /// <summary>
    /// Renders the state of the view model in the overlay (AC4, UX-DR-22).
    /// </summary>
    private void Render() => _message.Text = _viewModel.CancelConfirmationText;
}
