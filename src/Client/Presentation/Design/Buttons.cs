using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LANPartyHub.Client.Presentation.Design;

/// <summary>
/// The buttons of the interface, by their ROLE (DESIGN, UX-DR-7, UX-DR-8, AC1, AC2).
///
/// <para>
/// La JERARQUÍA DE ACCIONES vive aquí y no en cada vista: una superficie tiene UNA acción
/// primaria, la secundaria no compite con ella, la de peligro está reservada y la fantasma
/// es la terciaria (UX-DR-8). Un botón escrito a mano en una vista puede saltarse la
/// jerarquía; uno hecho con esta fábrica no (AC1, AC2).
/// </para>
/// <para>
/// El alto NO cambia con el foco: el foco engorda el borde por dentro del alto fijo, así
/// que la rejilla de la superficie no se deforma al pasar el ratón (UX-DR-7).
/// </para>
/// </summary>
public static class Buttons
{
    /// <summary>
    /// The PRIMARY action of a surface: the one that the player can press right now
    /// (UX-DR-7).
    /// </summary>
    /// <param name="text">Text of the action, when it is known at creation.</param>
    /// <returns>The button of the primary action.</returns>
    public static Button Primary(string? text = null)
    {
        var button = Build(text, new SolidColorBrush(DesignTokens.AccentAction), new SolidColorBrush(DesignTokens.AccentActionInk), DesignTokens.ButtonHeight);

        button.BorderThickness = new Thickness(0);

        return button;
    }

    /// <summary>
    /// The SECONDARY action: it acts without competing with the primary one (UX-DR-8).
    /// </summary>
    /// <param name="text">Text of the action, when it is known at creation.</param>
    /// <returns>The button of the secondary action.</returns>
    public static Button Secondary(string? text = null)
    {
        var button = Build(text, new SolidColorBrush(DesignTokens.SurfaceOverlay), new SolidColorBrush(DesignTokens.InkPrimary), DesignTokens.ButtonHeight);

        button.BorderThickness = new Thickness(1);
        button.BorderBrush = new SolidColorBrush(DesignTokens.BorderStrong);

        return button;
    }

    /// <summary>
    /// The DANGER action: it is reserved to cancelling a download, deleting an
    /// installation or discarding an error (UX-DR-8).
    /// </summary>
    /// <param name="text">Text of the action, when it is known at creation.</param>
    /// <returns>The button of the dangerous action.</returns>
    public static Button Danger(string? text = null)
    {
        var button = Build(text, new SolidColorBrush(DesignTokens.StateDanger), new SolidColorBrush(DesignTokens.SurfaceBase), DesignTokens.ButtonHeight);

        button.BorderThickness = new Thickness(0);

        return button;
    }

    /// <summary>
    /// The GHOST action: the tertiary one, which is never the only way to a result
    /// (UX-DR-8).
    /// </summary>
    /// <param name="text">Text of the action, when it is known at creation.</param>
    /// <returns>The button of the tertiary action.</returns>
    public static Button Ghost(string? text = null)
    {
        var button = Build(text, Brushes.Transparent, new SolidColorBrush(DesignTokens.InkSecondary), DesignTokens.GhostButtonHeight);

        button.BorderThickness = new Thickness(0);

        return button;
    }

    /// <summary>
    /// Builds a button with the geometry of the design: its height, its radius and its
    /// padding (DESIGN, UX-DR-4, UX-DR-7).
    /// </summary>
    /// <param name="text">Text of the action, when it is known at creation.</param>
    /// <param name="background">Background of the button.</param>
    /// <param name="foreground">Foreground of the button.</param>
    /// <param name="height">Height of the button, in pixels.</param>
    /// <returns>The button.</returns>
    private static Button Build(string? text, IBrush background, IBrush foreground, double height)
    {
        var button = new Button
        {
            Content = text,
            Height = height,
            MinHeight = height,
            Padding = new Thickness(DesignTokens.ButtonPaddingHorizontal, 0, DesignTokens.ButtonPaddingHorizontal, 0),
            CornerRadius = new CornerRadius(DesignTokens.ButtonRadius),
            Background = background,
            Foreground = foreground,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        return button;
    }
}
