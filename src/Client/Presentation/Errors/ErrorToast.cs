using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Errors;

/// <summary>
/// Notice of error of the client, con sus TRES partes (AC3, UX-DR-36, DESIGN).
///
/// <para>
/// El aviso es una barra de superficie de peligro con el texto del error dentro, y el
/// texto son las tres partes del catálogo: qué falló, por qué y qué hacer ahora. NO lleva
/// iconografía: el texto es el aviso (DESIGN, UX-DR-36).
/// </para>
/// <para>
/// El aviso NO sustituye a la persistencia del error en el registro: el log es el sumidero
/// durable del diagnóstico (AD-15, UX-DR-36).
/// </para>
/// </summary>
public sealed class ErrorToast : Border
{
    private readonly TextBlock _message;
    private readonly TextBlock _cause;
    private readonly TextBlock _nextStep;

    /// <summary>
    /// Creates the notice of error of the client.
    /// </summary>
    /// <param name="viewModel">View model of the error, con sus tres partes.</param>
    public ErrorToast(ErrorMessageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Margin = new Thickness(16, 16, 16, 0);
        Padding = new Thickness(12);
        CornerRadius = new CornerRadius(8);
        Background = new SolidColorBrush(DesignTokens.StateDanger);
        IsVisible = false;

        // La tinta del aviso es la de la superficie base, porque el fondo es el color del
        // peligro (DESIGN).
        var foreground = new SolidColorBrush(DesignTokens.SurfaceBase);

        _message = new TextBlock
        {
            Text = viewModel.Message,
            Foreground = foreground,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        _cause = new TextBlock
        {
            Text = viewModel.Cause,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
        };

        _nextStep = new TextBlock
        {
            Text = viewModel.NextStep,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
        };

        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Children =
            {
                _message,
                _cause,
                _nextStep,
            },
        };
    }

    /// <summary>
    /// The text of WHAT FAILED, so it can be inspected and driven from the outside (AC3).
    /// </summary>
    public TextBlock MessageText => _message;

    /// <summary>
    /// The text of WHY it failed, so it can be inspected and driven from the outside
    /// (AC3).
    /// </summary>
    public TextBlock CauseText => _cause;

    /// <summary>
    /// The text of WHAT TO DO NOW, so it can be inspected and driven from the outside
    /// (AC3).
    /// </summary>
    public TextBlock NextStepText => _nextStep;

    /// <summary>
    /// Shows the notice of error, con sus tres partes (AC3).
    /// </summary>
    /// <param name="viewModel">View model of the error.</param>
    public void Show(ErrorMessageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _message.Text = viewModel.Message;
        _cause.Text = viewModel.Cause;
        _nextStep.Text = viewModel.NextStep;

        IsVisible = true;
    }

    /// <summary>
    /// Hides the notice of error (AC3).
    /// </summary>
    public void Hide() => IsVisible = false;
}
