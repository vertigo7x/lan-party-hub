using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Presentation.Install;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The confirmation of a PRIVILEGED operation: `Esc` cancels the non destructive action and
/// gives the focus BACK to the element that invoked it (AC2, UX-DR-16, UX-DR-37).
///
/// <para>
/// El foco es el CORAZÓN del contrato: el `KeyDown` de Avalonia sólo llega al elemento
/// ENFOCADO, así que el overlay tiene que TOMAR el foco al abrirse y devolverlo al
/// INVOCADOR al cerrarse. Con el foco quieto en el invocador, el `Esc` nunca llega al
/// diálogo y la confirmación NO se puede cancelar (AC2, UX-DR-37).
/// </para>
/// <para>
/// Los tests usan la plataforma HEADLESS de Avalonia, que enruta las teclas de verdad, así
/// que comprueban el CAMINO COMPLETO del foco y no una suposición sobre él.
/// </para>
/// </summary>
/// <para>
/// La prueba vive en la COLECCIÓN de las superficies de Avalonia, que NO corre en
/// paralelo: la plataforma headless tiene UN dispatcher con un hilo propio y dos pruebas
/// que lo usen a la vez se pisan (AC2, UX-DR-37).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class PrivilegedOperationOverlayTests
{
    /// <summary>
    /// The deadline for the DECISION of the player, para que un `Esc` que NO llega al
    /// diálogo FALLE el test en vez de COLGARLO (AC2, UX-DR-37).
    /// </summary>
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The overlay TAKES the focus when it opens, para que el `KeyDown` llegue al handler
    /// del diálogo y no al elemento que estaba detrás (AC2, UX-DR-37).
    /// </summary>
    [AvaloniaFact]
    public void The_overlay_takes_the_focus_when_it_opens()
    {
        var invoker = new Button { Content = "Reintentar" };
        var window = CreateWindow(invoker);
        var dialog = CreateDialog();

        window.Show();

        Dispatcher.UIThread.RunJobs();

        Assert.True(invoker.Focus());

        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        Dispatcher.UIThread.RunJobs();

        // El foco está en el DIÁLOGO: el `KeyDown` de Avalonia sólo llega al elemento
        // ENFOCADO, así que sin esto el `Esc` no cancelaría nada (AC2, UX-DR-37).
        Assert.NotSame(invoker, window.FocusManager!.GetFocusedElement());
        Assert.Same(dialog.CancelButton, window.FocusManager.GetFocusedElement());

        window.Close();
    }

    /// <summary>
    /// The `Esc` CANCELS the confirmation, y la decisión es un NO (AC2, UX-DR-37).
    /// </summary>
    [AvaloniaFact]
    public async Task The_escape_cancels_the_confirmation_of_the_privileged_operation()
    {
        var invoker = new Button { Content = "Reintentar" };
        var window = CreateWindow(invoker);
        var dialog = CreateDialog();

        window.Show();

        Dispatcher.UIThread.RunJobs();

        Assert.True(invoker.Focus());

        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");

        // El `Esc` llegó al diálogo porque el foco estaba dentro de él, así que la
        // confirmación se canceló y la decisión es un NO (AC2, UX-DR-37).
        //
        // La espera tiene PLAZO porque, sin el foco dentro del diálogo, el `Esc` NO
        // llega al handler y la decisión NO se resuelve: sin plazo, el test colgaría
        // en vez de FALLAR (AC2, UX-DR-37).
        Assert.False(await dialog.Decision.WaitAsync(DecisionTimeout, TestContext.Current.CancellationToken));
        Assert.False(overlay.IsOpen);

        window.Close();
    }

    /// <summary>
    /// The `Esc` gives the focus BACK to the element that invoked the confirmation
    /// (AC2, UX-DR-37).
    /// </summary>
    [AvaloniaFact]
    public void The_escape_gives_the_focus_back_to_the_invoker()
    {
        var invoker = new Button { Content = "Reintentar" };
        var window = CreateWindow(invoker);
        var dialog = CreateDialog();

        window.Show();

        Dispatcher.UIThread.RunJobs();

        Assert.True(invoker.Focus());

        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");

        // El foco vuelve al INVOCADOR cuando el overlay se cierra: el jugador sigue
        // donde estaba y no pierde el sitio (AC2, UX-DR-37).
        Assert.Same(invoker, window.FocusManager!.GetFocusedElement());

        window.Close();
    }

    /// <summary>
    /// The action that CONFIRMS also gives the focus back to the invoker (AC2, UX-DR-37).
    /// </summary>
    [AvaloniaFact]
    public async Task The_confirmation_gives_the_focus_back_to_the_invoker()
    {
        var invoker = new Button { Content = "Reintentar" };
        var window = CreateWindow(invoker);
        var dialog = CreateDialog();

        window.Show();

        Dispatcher.UIThread.RunJobs();

        Assert.True(invoker.Focus());

        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        Dispatcher.UIThread.RunJobs();

        dialog.ConfirmButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.True(await dialog.Decision.WaitAsync(DecisionTimeout, TestContext.Current.CancellationToken));
        Assert.Same(invoker, window.FocusManager!.GetFocusedElement());
        Assert.False(overlay.IsOpen);

        window.Close();
    }

    /// <summary>
    /// The action that CANCELS also gives the focus back to the invoker (AC2, UX-DR-37).
    /// </summary>
    [AvaloniaFact]
    public async Task The_cancellation_gives_the_focus_back_to_the_invoker()
    {
        var invoker = new Button { Content = "Reintentar" };
        var window = CreateWindow(invoker);
        var dialog = CreateDialog();

        window.Show();

        Dispatcher.UIThread.RunJobs();

        Assert.True(invoker.Focus());

        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        Dispatcher.UIThread.RunJobs();

        dialog.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.False(await dialog.Decision.WaitAsync(DecisionTimeout, TestContext.Current.CancellationToken));
        Assert.Same(invoker, window.FocusManager!.GetFocusedElement());
        Assert.False(overlay.IsOpen);

        window.Close();
    }

    /// <summary>
    /// The overlay lives INSIDE the window of the player, y no abre una ventana nueva
    /// (AC2, UX-DR-16, UX-DR-23).
    /// </summary>
    [AvaloniaFact]
    public void The_overlay_lives_inside_the_window_of_the_player()
    {
        var invoker = new Button { Content = "Reintentar" };
        var window = CreateWindow(invoker);
        var dialog = CreateDialog();

        window.Show();

        Dispatcher.UIThread.RunJobs();

        var overlay = new PrivilegedOperationOverlay(dialog, window);

        overlay.Show();

        // El overlay es un hijo del PANEL de la ventana, no una ventana propia: un diálogo
        // nunca se apila sobre otro (UX-DR-16, UX-DR-23).
        Assert.Same(dialog, Assert.Single(overlay.Overlay.Children));
        Assert.True(overlay.IsOpen);

        window.Close();
    }

    /// <summary>
    /// Builds the window of the player with the element INVOCADOR dentro de ella.
    /// </summary>
    /// <param name="invoker">Element that invokes the confirmation.</param>
    /// <returns>The window of the player.</returns>
    private static Window CreateWindow(Control invoker)
    {
        var panel = new StackPanel
        {
            Children =
            {
                invoker,
            },
        };

        return new Window { Content = panel };
    }

    /// <summary>
    /// Builds the overlay de confirmación de la operación privilegiada.
    /// </summary>
    /// <returns>The dialog de confirmación.</returns>
    private static PrivilegedOperationDialog CreateDialog() =>
        new("writeConfig", "El manifiesto va a escribir dentro de la raíz de instalación.");
}
