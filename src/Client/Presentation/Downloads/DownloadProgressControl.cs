using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Downloads;

/// <summary>
/// Control of the progress of a download operation (AC2, AC3, AC4, UX-DR-5, UX-DR-13,
/// UX-DR-22).
///
/// <para>
/// La barra y el texto salen del MISMO view model, así que la barra dibuja EXACTAMENTE la
/// fracción que el porcentaje del texto declara: nunca se contradicen (UX-DR-5,
/// UX-DR-13).
/// </para>
/// <para>
/// Las acciones «Pausar», «Reanudar» y «Cancelar» se habilitan según el estado de la
/// operación, y la «Cancelar» es la acción PELIGRO: se reserva a cancelar una descarga y
/// exige confirmación porque destruye trabajo no recuperable (UX-DR-8, UX-DR-22).
/// </para>
/// </summary>
public sealed class DownloadProgressControl : UserControl
{
    private readonly DownloadProgressViewModel _viewModel;
    private readonly ProgressBar _bar;
    private readonly TextBlock _stateText;
    private readonly TextBlock _progressText;
    private readonly Button _pauseButton;
    private readonly Button _resumeButton;
    private readonly Button _cancelButton;

    /// <summary>Creates the control of the progress of a download.</summary>
    /// <param name="viewModel">View model of the progress.</param>
    public DownloadProgressControl(DownloadProgressViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        _stateText = new TextBlock
        {
            FontSize = DesignTokens.MetaFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
        };

        _progressText = new TextBlock();

        // UX-DR-13: la barra siempre va acompañada del texto «&lt;n&gt; %», así que la
        // barra no es accesible por sí sola.
        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 8,
        };

        _pauseButton = BuildActionButton(
            DownloadProgressViewModel.PauseActionText,
            DesignTokens.InkPrimary);

        _resumeButton = BuildActionButton(
            DownloadProgressViewModel.ResumeActionText,
            DesignTokens.AccentAction);

        // UX-DR-8: «button-danger» se reserva a cancelar una descarga. El color de
        // peligro no sustituye nunca al texto de la acción.
        _cancelButton = BuildActionButton(
            DownloadProgressViewModel.CancelActionText,
            DesignTokens.StateWarning);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Descarga",
                    FontSize = DesignTokens.TitleFontSize,
                    FontWeight = FontWeight.SemiBold,
                },
                _stateText,
                _bar,
                _progressText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        _pauseButton,
                        _resumeButton,
                        _cancelButton,
                    },
                },
            },
        };

        _viewModel.Changed += Render;

        Render();
    }

    /// <summary>
    /// The bar of the progress, so it can be inspected and driven from the outside
    /// (UX-DR-13).
    /// </summary>
    public ProgressBar Bar => _bar;

    /// <summary>
    /// The text of the progress, so it can be inspected and driven from the outside
    /// (UX-DR-5).
    /// </summary>
    public TextBlock ProgressText => _progressText;

    /// <summary>
    /// The action that pauses the download, so it can be inspected and driven from the
    /// outside (UX-DR-6).
    /// </summary>
    public Button PauseButton => _pauseButton;

    /// <summary>
    /// The action that resumes the download, so it can be inspected and driven from the
    /// outside (UX-DR-6).
    /// </summary>
    public Button ResumeButton => _resumeButton;

    /// <summary>
    /// The action that cancels the download, so it can be inspected and driven from the
    /// outside (UX-DR-8, UX-DR-22).
    /// </summary>
    public Button CancelButton => _cancelButton;

    /// <summary>
    /// Renders the state of the view model in the control (AC2, AC3, AC4).
    /// </summary>
    private void Render()
    {
        _stateText.Text = _viewModel.StateText;
        _progressText.Text = _viewModel.ProgressText;

        // UX-DR-13: la barra dibuja la MISMA fracción que el porcentaje declara.
        _bar.Value = _viewModel.BarFraction * 100;

        _pauseButton.IsEnabled = _viewModel.CanPause;
        _resumeButton.IsEnabled = _viewModel.CanResume;
        _cancelButton.IsEnabled = _viewModel.CanCancel;
    }

    /// <summary>
    /// Builds one action of the control (UX-DR-8).
    /// </summary>
    /// <param name="text">Text of the action.</param>
    /// <param name="foreground">Color of the text of the action.</param>
    /// <returns>The action of the control.</returns>
    private static Button BuildActionButton(string text, Color foreground) => new()
    {
        Content = text,
        Height = DesignTokens.FieldInputHeight,
        Foreground = new SolidColorBrush(foreground),
        IsEnabled = false,
    };
}
