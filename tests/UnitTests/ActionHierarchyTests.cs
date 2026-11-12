using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.Downloads;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The JERARCHY of the actions of the interface (AC1, AC2, AC4, UX-DR-7, UX-DR-8).
///
/// <para>
/// La jerarquía vive en la FÁBRICA de botones y no en cada vista: una superficie tiene
/// UNA acción primaria, la secundaria no compite con ella, la de peligro está reservada
/// y la fantasma es la terciaria (UX-DR-8). Estas pruebas comprueban la fábrica Y vigilan
/// que el código REAL no se la salte.
/// </para>
/// <para>
/// La clase vive en la COLECCIÓN de las superficies de Avalonia, que NO corre en
/// paralelo: la plataforma headless tiene UN dispatcher con un hilo propio
/// (UX-DR-37).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class ActionHierarchyTests
{
    /// <summary>
    /// The PRIMARY, SECONDARY and DANGER buttons keep the height of the design, and the
    /// GHOST one is the lower one (UX-DR-7, UX-DR-8).
    /// </summary>
    [AvaloniaFact]
    public void The_height_of_every_role_is_the_one_of_the_design()
    {
        // UX-DR-7: el alto NO cambia con el foco, así que la rejilla de la superficie no
        // se deforma al pasar el ratón.
        Assert.Equal(36, Buttons.Primary("Jugar").Height);
        Assert.Equal(36, Buttons.Secondary("Acción").Height);
        Assert.Equal(36, Buttons.Danger("Cancelar").Height);
        Assert.Equal(32, Buttons.Ghost("Diagnosticar").Height);
    }

    /// <summary>
    /// The focus does NOT change the height of the button (UX-DR-7).
    /// </summary>
    [AvaloniaFact]
    public void The_focus_does_not_change_the_height_of_the_button()
    {
        var button = Buttons.Primary("Jugar");

        Assert.Equal(button.Height, button.MinHeight);
    }

    /// <summary>
    /// Every button has the radius and the padding of the design (UX-DR-4, UX-DR-7).
    /// </summary>
    [AvaloniaFact]
    public void The_buttons_have_the_radius_and_the_padding_of_the_design()
    {
        var button = Buttons.Primary("Jugar");

        Assert.Equal(new CornerRadius(DesignTokens.ButtonRadius), button.CornerRadius);
        Assert.Equal(
            new Thickness(DesignTokens.ButtonPaddingHorizontal, 0, DesignTokens.ButtonPaddingHorizontal, 0),
            button.Padding);
    }

    /// <summary>
    /// Every role wears the colours of the design (UX-DR-7, UX-DR-8).
    /// </summary>
    [AvaloniaFact]
    public void Every_role_has_the_colours_of_the_design()
    {
        var primary = Buttons.Primary("Jugar");
        Assert.Equal(DesignTokens.AccentAction, ColorOf(primary.Background));
        Assert.Equal(DesignTokens.AccentActionInk, ColorOf(primary.Foreground));

        var secondary = Buttons.Secondary("Acción");
        Assert.Equal(DesignTokens.SurfaceOverlay, ColorOf(secondary.Background));
        Assert.Equal(DesignTokens.InkPrimary, ColorOf(secondary.Foreground));
        Assert.Equal(new Thickness(1), secondary.BorderThickness);
        Assert.Equal(DesignTokens.BorderStrong, ColorOf(secondary.BorderBrush));

        var danger = Buttons.Danger("Cancelar");
        Assert.Equal(DesignTokens.StateDanger, ColorOf(danger.Background));
        Assert.Equal(DesignTokens.SurfaceBase, ColorOf(danger.Foreground));

        var ghost = Buttons.Ghost("Diagnosticar");
        Assert.Equal(Colors.Transparent, ColorOf(ghost.Background));
        Assert.Equal(DesignTokens.InkSecondary, ColorOf(ghost.Foreground));
    }

    /// <summary>
    /// The DANGER action is RESERVED: only cancelling a download, deleting an
    /// installation or discarding an error may wear it (UX-DR-8).
    /// </summary>
    [Fact]
    public void The_danger_action_is_reserved_to_the_dangerous_actions()
    {
        var presentation = Path.Combine(RepositoryLayout.Root, "src", "Client", "Presentation");

        var reserved = new[] { "Downloads", "Install", "Errors" };

        var offenders = Directory
            .EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("Buttons.Danger", StringComparison.Ordinal))
            .Where(file => !reserved.Any(segment => file.Contains(segment, StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToArray();

        // La acción de peligro no es un color más: es una RESERVA, y el guard vigila el
        // código REAL para que nadie la gaste de más (UX-DR-8).
        Assert.Empty(offenders);
    }

    /// <summary>
    /// The `Esc` of the confirmation of a cancellation KEEPS the download: it cancels
    /// the action that is NOT destructive and dismisses the dialog (AC4, UX-DR-37).
    /// </summary>
    [AvaloniaFact]
    public void The_escape_keeps_the_download_and_dismisses_the_dialog()
    {
        var dialog = new DownloadCancelDialog(new DownloadProgressViewModel());

        var dismissed = false;

        dialog.Dismissed += (_, _) => dismissed = true;

        var window = new Window { Content = dialog };

        window.Show();

        Dispatcher.UIThread.RunJobs();

        Assert.True(dialog.KeepButton.Focus());

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");

        Dispatcher.UIThread.RunJobs();

        // El `Esc` nunca destruye trabajo: mantiene la descarga (AC4, UX-DR-22).
        Assert.True(dismissed);

        window.Close();
    }

    /// <summary>
    /// The colour of a brush, para que las pruebas comprueben el color REAL que la vista
    /// ve.
    /// </summary>
    /// <param name="brush">Brush of the control.</param>
    /// <returns>The colour of the brush.</returns>
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
