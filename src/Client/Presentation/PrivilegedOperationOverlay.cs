using Avalonia.Controls;
using LANPartyHub.Client.Presentation.Install;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Presenter of the overlay de CONFIRMACIÓN de la operación privilegiada (AC2, UX-DR-16,
/// UX-DR-37).
///
/// <para>
/// El overlay vive DENTRO de la ventana del cliente y no abre una ventana nueva, porque un
/// diálogo nunca se apila sobre otro (UX-DR-16, UX-DR-23).
/// </para>
/// <para>
/// El foco es la CLAVE del contrato: el <c>KeyDown</c> de Avalonia sólo llega al elemento
/// ENFOCADO, así que el diálogo tiene que TOMAR el foco al abrirse y devolverlo al
/// elemento INVOCADOR al cerrarse. Si el foco se queda en el invocador mientras el overlay
/// está abierto, el <c>Esc</c> nunca llega al diálogo y la confirmación NO se puede
/// cancelar (AC2, UX-DR-37).
/// </para>
/// </summary>
public sealed class PrivilegedOperationOverlay
{
    private readonly Window _window;
    private readonly Grid _overlay;
    private readonly PrivilegedOperationDialog _dialog;
    private readonly Control? _invoker;

    /// <summary>
    /// Creates the presenter of the overlay de confirmación.
    /// </summary>
    /// <param name="dialog">Overlay de confirmación de la operación.</param>
    /// <param name="window">Window of the player, which hosts the overlay.</param>
    public PrivilegedOperationOverlay(PrivilegedOperationDialog dialog, Window window)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(window);

        _dialog = dialog;
        _window = window;
        _overlay = new Grid();

        // El invocador se APUNTA ANTES de que el overlay entre en el árbol visual: cuando
        // el overlay está abierto, el foco ya está en el diálogo y el invocador es el
        // elemento que lo tenía ANTES de abrirlo (AC2, UX-DR-37).
        _invoker = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control;

        // El overlay se cierra CUANDO HAY DECISIÓN, y no con un handler de `Esc` propio:
        // el diálogo ya marca el `Esc` como `Handled`, así que un segundo handler en el
        // MISMO elemento NUNCA se ejecutaría y el overlay se quedaría abierto con la
        // confirmación ya cancelada (AC2, UX-DR-37).
        _dialog.Dismissed += (_, _) => Close();

        _overlay.Children.Add(dialog);
    }

    /// <summary>
    /// The overlay que hosts the dialog, so it can be inspected from the outside
    /// (UX-DR-16).
    /// </summary>
    public Grid Overlay => _overlay;

    /// <summary>
    /// The dialog de confirmación que the overlay hosts (AC2).
    /// </summary>
    public PrivilegedOperationDialog Dialog => _dialog;

    /// <summary>
    /// The element that HAD the focus before the overlay opened, so it can receive it back
    /// when the overlay closes (AC2, UX-DR-37).
    /// </summary>
    public Control? Invoker => _invoker;

    /// <summary>
    /// Whether the overlay is OPEN, así que el jugador tiene la confirmación delante
    /// (AC2).
    /// </summary>
    public bool IsOpen => _overlay.Parent is Panel;

    /// <summary>
    /// Shows the overlay inside the window and gives the focus to the dialog, para que el
    /// <c>Esc</c> llegue a su handler (AC2, UX-DR-37).
    /// </summary>
    public void Show()
    {
        if (_window.Content is not Panel panel)
        {
            return;
        }

        panel.Children.Add(_overlay);

        // El foco lo toma la acción de CANCELAR y no el `UserControl` del diálogo, porque
        // un `UserControl` NO es enfocable por defecto en Avalonia y su `Focus()` no hace
        // nada. La acción de cancelar SÍ es un botón, así que la toma, y el `Esc` SUBE
        // desde ella hasta el handler del diálogo (AC2, UX-DR-37).
        //
        // El foco se pide en el `Loaded` de la acción, que es cuando el botón ya está en
        // el árbol VISUAL: antes de eso todavía no es enfocable y el `Focus()` se pierde.
        _dialog.CancelButton.Loaded += (_, _) => _dialog.CancelButton.Focus();
    }

    /// <summary>
    /// Closes the overlay and gives the focus BACK to the element that invoked it
    /// (AC2, UX-DR-37).
    /// </summary>
    public void Close()
    {
        if (_overlay.Parent is Panel parent)
        {
            parent.Children.Remove(_overlay);
        }

        // El foco vuelve al INVOCADOR cuando el overlay se cierra, no cuando se abre
        // (AC2, UX-DR-37).
        if (_invoker is not null)
        {
            _invoker.Focus();
        }
    }
}
