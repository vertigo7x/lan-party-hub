using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Install;

/// <summary>
/// Control of the progress of an installation operation (AC4, AC5, UX-DR-13, UX-DR-34).
///
/// <para>
/// La barra y el texto salen del MISMO view model, así que la barra dibuja EXACTAMENTE la
/// fracción que el porcentaje del texto declara: nunca se contradicen (UX-DR-5,
/// UX-DR-13).
/// </para>
/// <para>
/// La barra conserva su LONGITUD REAL en error: un fallo no pone la barra a cero, así que
/// el jugador ve hasta dónde llegó la instalación (UX-DR-13).
/// </para>
/// <para>
/// La acción «Reintentar» aparece cuando la operación quedó FALLIDA, porque la operación
/// es REINTENTABLE sin reinstalar el cliente (AC5, NFR-9, UX-DR-34).
/// </para>
/// </summary>
public sealed class InstallProgressControl : UserControl
{
    private readonly InstallProgressViewModel _viewModel;
    private readonly TextBlock _phaseText;
    private readonly TextBlock _progressText;
    private readonly ProgressBar _bar;
    private readonly TextBlock _errorText;
    private readonly Button _retryButton;

    /// <summary>Creates the control of the progress of an installation.</summary>
    /// <param name="viewModel">View model of the progress.</param>
    public InstallProgressControl(InstallProgressViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        _phaseText = new TextBlock
        {
            FontSize = DesignTokens.MetaFontSize,
            Foreground = new SolidColorBrush(DesignTokens.InkSecondary),
        };

        _progressText = new TextBlock();

        // UX-DR-13: la barra siempre va acompañada del texto «<n> %», así que la barra
        // no es accesible por sí sola.
        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 8,
        };

        // AC5, UX-DR-36: el error de la operación se presenta con sus TRES partes, y el
        // aviso de error lleva la superficie del peligro (DESIGN).
        _errorText = new TextBlock
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(DesignTokens.StateDanger),
        };

        _retryButton = new Button
        {
            Content = InstallProgressViewModel.RetryActionText,
            Height = DesignTokens.FieldInputHeight,
            Foreground = new SolidColorBrush(DesignTokens.AccentAction),
            IsEnabled = false,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(DesignTokens.Spacing4),
            Spacing = DesignTokens.Spacing2,
            Children =
            {
                new TextBlock
                {
                    Text = "Instalación",
                    FontSize = DesignTokens.TitleFontSize,
                    FontWeight = DesignTokens.TitleFontWeight,
                },
                _phaseText,
                _bar,
                _progressText,
                _errorText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = DesignTokens.Spacing2,
                    Children =
                    {
                        _retryButton,
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
    /// (AC4, UX-DR-34).
    /// </summary>
    public TextBlock ProgressText => _progressText;

    /// <summary>
    /// The text of the phase, so it can be inspected and driven from the outside (AC4).
    /// </summary>
    public TextBlock PhaseText => _phaseText;

    /// <summary>
    /// The text of the error de la operación con sus TRES partes, so it can be inspected
    /// and driven from the outside (AC5, UX-DR-36).
    /// </summary>
    public TextBlock ErrorText => _errorText;

    /// <summary>
    /// The action that retries a failed installation, so it can be inspected and driven
    /// from the outside (AC5, UX-DR-34).
    /// </summary>
    public Button RetryButton => _retryButton;

    /// <summary>
    /// Renders the state of the view model in the control (AC4, AC5).
    /// </summary>
    private void Render()
    {
        _phaseText.Text = _viewModel.PhaseText;

        _progressText.Text = _viewModel.ProgressText;

        // UX-DR-13: la barra dibuja la MISMA fracción que el porcentaje declara, y
        // conserva su longitud REAL en error.
        _bar.Value = _viewModel.BarFraction * 100;

        // AC5, UX-DR-36: el error se presenta con sus TRES partes: qué falló, por qué y
        // qué hacer ahora.
        var error = _viewModel.Error;

        _errorText.Text = error?.Text ?? string.Empty;
        _errorText.IsVisible = error is not null;

        // AC5, UX-DR-34: «Reintentar» sólo existe cuando la operación es reintentable.
        _retryButton.IsEnabled = _viewModel.CanRetry;
        _retryButton.IsVisible = _viewModel.CanRetry;
    }
}
