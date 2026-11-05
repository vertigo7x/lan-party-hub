using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Presentation.Design;

namespace LANPartyHub.Client.Presentation.Install;

/// <summary>
/// Overlay de CONFIRMACIÓN de una operación privilegiada de la instalación (AC2,
/// UX-DR-16, UX-DR-37).
///
/// <para>
/// El overlay es un <c>dialog-surface</c> de UN SOLO NIVEL que vive DENTRO de la ventana
/// del cliente: no se abre una ventana nueva, porque un diálogo nunca se apila sobre otro
/// y el cliente es una aplicación de escritorio con una sola ventana (UX-DR-16,
/// UX-DR-23).
/// </para>
/// <para>
/// El cuerpo NOMBRA lo que la operación escribe, así que el jugador aprueba algo
/// CONCRETO y no un «sí» a ciegas (AC2).
/// </para>
/// <para>
/// <c>Esc</c> CANCELA la acción, que no es destructiva, y el foco vuelve al elemento
/// INVOCADOR (AC2, UX-DR-37).
/// </para>
/// </summary>
public sealed class PrivilegedOperationDialog : UserControl
{
    /// <summary>Text of the action that confirms the privileged operation (AC2).</summary>
    public const string ConfirmActionText = "Continuar";

    /// <summary>Text of the action that cancels the privileged operation (AC2).</summary>
    public const string CancelActionText = "Cancelar";

    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Border _surface;
    private readonly TextBlock _message;
    private readonly Button _confirmButton;
    private readonly Button _cancelButton;

    /// <summary>Creates the overlay de confirmación de la operación privilegiada.</summary>
    /// <param name="operation">
    /// Name of the operation of the manifest that is waiting for the confirmation.
    /// </param>
    /// <param name="detail">
    /// Description of what the operation writes, so the dialog NAMES what the player is
    /// approving.
    /// </param>
    public PrivilegedOperationDialog(string operation, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        Operation = operation;

        _message = new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
        };

        _confirmButton = new Button
        {
            Content = ConfirmActionText,
            Height = DesignTokens.FieldInputHeight,
            Foreground = new SolidColorBrush(DesignTokens.AccentAction),
        };

        // La acción que cancela NO es la acción PELIGRO: cancelar una operación
        // privilegiada NO destruye trabajo, así que usa el tinta primaria (UX-DR-8).
        _cancelButton = new Button
        {
            Content = CancelActionText,
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
                        Text = "Confirmación necesaria",
                        FontSize = DesignTokens.TitleFontSize,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = $"El manifiesto declara la operación «{operation}», que escribe dentro de la raíz de instalación.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _message,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            _cancelButton,
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

        _confirmButton.Click += (_, _) => Resolve(true);
        _cancelButton.Click += (_, _) => Resolve(false);

        // UX-DR-37: `Esc` cancela la acción NO destructiva y devuelve el foco al
        // elemento invocador.
        KeyDown += OnKeyDown;
    }

    /// <summary>Name of the operation of the manifest that is waiting for the confirmation.</summary>
    public string Operation { get; }

    /// <summary>
    /// The surface of the dialog, so it can be inspected and driven from the outside
    /// (UX-DR-16).
    /// </summary>
    public Border Surface => _surface;

    /// <summary>
    /// The message that names what the operation writes, so it can be inspected and
    /// driven from the outside (AC2).
    /// </summary>
    public TextBlock Message => _message;

    /// <summary>
    /// The action that confirms the operation, so it can be inspected and driven from the
    /// outside (AC2).
    /// </summary>
    public Button ConfirmButton => _confirmButton;

    /// <summary>
    /// The action that cancels the operation, so it can be inspected and driven from the
    /// outside (AC2, UX-DR-37).
    /// </summary>
    public Button CancelButton => _cancelButton;

    /// <summary>
    /// The DECISION of the player: <c>true</c> when the player confirmed and
    /// <c>false</c> when the player declined or dismissed the confirmation (AC2).
    /// </summary>
    public Task<bool> Decision => _decision.Task;

    /// <summary>
    /// Occurs when the player has ALREADY decided, así que el overlay que hospeda al
    /// diálogo puede cerrarse y devolver el foco al elemento invocador (AC2, UX-DR-37).
    ///
    /// <para>
    /// El aviso es el CAMINO ÚNICO de cierre, porque el `Esc` marca el `KeyDown` como
    /// `Handled` y un segundo handler en el MISMO elemento nunca se ejecutaría (AC2).
    /// </para>
    /// </summary>
    public event EventHandler<bool>? Dismissed;

    /// <summary>
    /// Cancels the confirmation, as the <c>Esc</c> does (AC2, UX-DR-37).
    /// </summary>
    public void Decline() => Resolve(false);

    /// <summary>
    /// Confirms the operation, as the action that confirms does (AC2).
    /// </summary>
    public void Confirm() => Resolve(true);

    /// <summary>
    /// Handles the `Esc`, que cancela la acción no destructiva (AC2, UX-DR-37).
    /// </summary>
    /// <param name="sender">Sender of the event.</param>
    /// <param name="eventArgs">Arguments of the event.</param>
    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape)
        {
            return;
        }

        eventArgs.Handled = true;

        Decline();
    }

    /// <summary>
    /// Records the DECISION of the player and notifies that the dialog was DISMISSED, para
    /// que el overlay se cierre y devuelva el foco al elemento invocador (AC2, UX-DR-37).
    /// </summary>
    /// <param name="confirmed">Whether the player confirmed the operation.</param>
    private void Resolve(bool confirmed)
    {
        if (!_decision.TrySetResult(confirmed))
        {
            return;
        }

        Dismissed?.Invoke(this, confirmed);
    }
}

/// <summary>
/// Adapter of the confirmation of a privileged operation that goes through the overlay
/// (AC2, UX-DR-16, UX-DR-37).
///
/// <para>
/// El puerto se pide en el MOMENTO y la decisión vive SÓLO en memoria: al reanudar una
/// operación tras un reinicio del cliente se vuelve a pedir (AC3).
/// </para>
/// </summary>
public sealed class DialogPrivilegedOperationConfirmation : IPrivilegedOperationConfirmation
{
    private readonly Func<string, string, PrivilegedOperationDialog> _factory;
    private readonly Lock _gate = new();

    private Func<PrivilegedOperationDialog, Task>? _show;

    /// <summary>Creates the adapter of the confirmation.</summary>
    /// <param name="factory">
    /// Builds the overlay de confirmación de la operación, con su nombre y su detalle.
    /// </param>
    public DialogPrivilegedOperationConfirmation(
        Func<string, string, PrivilegedOperationDialog> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    /// <summary>
    /// Sets the PRESENTER of the overlay, que es el que lo muestra dentro de la ventana
    /// y devuelve el foco al invocador (AC2, UX-DR-37).
    ///
    /// <para>
    /// El presentador se fija DESPUÉS de construir la composición, porque la ventana del
    /// cliente no existe todavía en el arranque (AD-1).
    /// </para>
    /// </summary>
    /// <param name="show">
    /// Shows the overlay and awaits the DECISION of the player, or <c>null</c> when the
    /// confirmation has no presenter.
    /// </param>
    public void SetPresenter(Func<PrivilegedOperationDialog, Task>? show)
    {
        lock (_gate)
        {
            _show = show;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        string operation,
        string detail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        Func<PrivilegedOperationDialog, Task>? show;

        lock (_gate)
        {
            show = _show;
        }

        var dialog = _factory(operation, detail);

        if (show is not null)
        {
            await show(dialog).ConfigureAwait(false);
        }

        // La decisión vive SÓLO en memoria: no se persiste como concedida, así que al
        // reanudar se vuelve a pedir (AC3).
        return await dialog.Decision.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
