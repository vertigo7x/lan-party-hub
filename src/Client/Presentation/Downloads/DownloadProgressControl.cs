using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    private readonly TextBlock _errorText;
    private readonly TextBlock _blockedReasonText;
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

        // AC3, UX-DR-36: el error de la operación se presenta con sus TRES partes, y el
        // aviso de error lleva la superficie del peligro (DESIGN).
        _errorText = new TextBlock
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(DesignTokens.StateDanger),
        };

        // AC1, UX-DR-33: el motivo del bloqueo del alta es VISIBLE, no un renglón oculto
        // en el registro.
        _blockedReasonText = new TextBlock
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(DesignTokens.StateDanger),
        };

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
            Margin = new Thickness(DesignTokens.Spacing4),
            Spacing = DesignTokens.Spacing2,
            Children =
            {
                new TextBlock
                {
                    Text = "Descarga",
                    FontSize = DesignTokens.TitleFontSize,
                    FontWeight = DesignTokens.TitleFontWeight,
                },
                _stateText,
                _bar,
                _progressText,
                _errorText,
                _blockedReasonText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = DesignTokens.Spacing2,
                    Children =
                    {
                        _pauseButton,
                        _resumeButton,
                        _cancelButton,
                    },
                },
            },
        };

        // El `Espacio` pausa o reanuda la descarga ENFOCADA (AC3, UX-DR-11). El manejador
        // va en fase de TUNEL porque el boton focused se come la tecla antes de que la
        // fase de burbuja llegue al control.
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        _viewModel.Changed += Render;

        // El anuncio va DESPUÉS del repintado, así que el lector dice lo mismo que ve
        // el jugador (AC4, UX-DR-39).
        _viewModel.Changed += () => AnnounceSignificantProgress();

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
    /// The text of the state, so it can be inspected and driven from the outside
    /// (AC1, UX-DR-6).
    /// </summary>
    public TextBlock StateText => _stateText;

    /// <summary>
    /// The text of the error of la operación con sus TRES partes, so it can be inspected
    /// and driven from the outside (AC3, UX-DR-36).
    /// </summary>
    public TextBlock ErrorText => _errorText;

    /// <summary>
    /// The VISIBLE reason of the block del alta de la instalación, so it can be inspected
    /// and driven from the outside (AC1, UX-DR-33).
    /// </summary>
    public TextBlock BlockedReasonText => _blockedReasonText;

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
    /// <summary>
    /// Announces the SIGNIFICANT step of the progress to the screen reader, y no cada
    /// `tick` (AC4, UX-DR-39).
    /// </summary>
    private void AnnounceSignificantProgress()
    {
        if (!_viewModel.ConsumeSignificantPercentage())
        {
            return;
        }

        // El nombre accesible lleva el porcentaje ANUNCIADO, así que el lector dice lo
        // mismo que ve el jugador (AC4, UX-DR-39).
        AutomationProperties.SetName(
            _bar,
            $"{_viewModel.AnnouncedPercentage} % {_viewModel.ProgressText}");
    }

    private void Render()
    {
        // AC1, AC2: el estado de una validación fallida es el DIFERENCIADO, con el tamaño
        // del paquete al final, y va en el color del peligro (UX-DR-33).
        _stateText.Text = _viewModel.StateTextWithSize;

        _stateText.Foreground = _viewModel.HasValidationFailure
            ? new SolidColorBrush(DesignTokens.StateDanger)
            : new SolidColorBrush(DesignTokens.InkSecondary);

        // AC2, UX-DR-45: el texto lleva la microcopy de la reanudación cuando la
        // operación se está reanudando, y el porcentaje y el par de tamaños siguen
        // saliendo de UNA SOLA fuente, así que la barra nunca contradice al texto
        // (UX-DR-5, UX-DR-13).
        _progressText.Text = _viewModel.ProgressTextWithResume;

        // UX-DR-13: la barra dibuja la MISMA fracción que el porcentaje declara.
        _bar.Value = _viewModel.BarFraction * 100;

        // AC3, UX-DR-36: el error se presenta con sus TRES partes: qué falló, por qué y
        // qué hacer ahora.
        var error = _viewModel.Error;

        _errorText.Text = error?.Text ?? string.Empty;
        _errorText.IsVisible = error is not null;

        // AC1, UX-DR-33: el motivo del bloqueo del alta es VISIBLE.
        _blockedReasonText.Text = _viewModel.InstallBlockedReason ?? string.Empty;
        _blockedReasonText.IsVisible = _viewModel.IsInstallBlocked;

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

    /// <summary>
    /// The `Espacio` PAUSES or RESUMES the focused download (AC3, UX-DR-11).
    ///
    /// <para>
    /// La primitiva actúa sobre la descarga ENFOCADA: pausa si puede pausar y reanuda si
    /// está pausada. Nunca hace las dos cosas ni actsúa sobre una descarga que no está
    /// enfocada (AC3).
    /// </para>
    /// </summary>
    /// <param name="sender">Sender of the event.</param>
    /// <param name="eventArgs">Arguments of the event.</param>
    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Space || eventArgs.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        // La primitiva actúa sobre la descarga ENFOCADA y sobre ninguna otra: si el foco
        // está en otro control, la tecla no es de esta descarga (AC3).
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

        if (focused is not null && focused != this && focused != _pauseButton && focused != _resumeButton)
        {
            return;
        }

        var action = _viewModel.CanPause
            ? _pauseButton
            : _viewModel.CanResume
                ? _resumeButton
                : null;

        if (action is null || !action.IsEnabled)
        {
            return;
        }

        eventArgs.Handled = true;

        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
