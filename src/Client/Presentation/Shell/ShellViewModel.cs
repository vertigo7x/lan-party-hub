using Avalonia.Input;

namespace LANPartyHub.Client.Presentation.Shell;

/// <summary>
/// The SEVEN surfaces of the client (AC1, UX-DR-9, UX-DR-10).
///
/// <para>
/// La arquitectura de información del cliente tiene SIETE superficies, y la navegación
/// es una COLUMNA, no una barra inferior. La columna de estado es la ÚNICA que está
/// siempre a la vista, porque es la que dice si la conexión vive.
/// </para>
/// </summary>
public enum ClientSurface
{
    /// <summary>The surface of the CONNECTION (the one that opens the app).</summary>
    Conexion,

    /// <summary>The surface of the CATALOG.</summary>
    Catalogo,

    /// <summary>The FICHA of a game, which opens from its card.</summary>
    Ficha,

    /// <summary>The surface of the DOWNLOADS.</summary>
    Descargas,

    /// <summary>The surface of the LIBRARY of the player.</summary>
    Biblioteca,

    /// <summary>The surface of the SETTINGS AND DIAGNOSTICS.</summary>
    Configuracion,

    /// <summary>The PERSISTENT column of the state of the connection.</summary>
    PanelDeEstado,
}

/// <summary>
/// The NAVIGATION of the client: which surface is in front and how to move between them
/// (AC1, AC2, AC3).
///
/// <para>
/// El modelo no depende de Avalonia: la navegación se puede probar SIN levantar una
/// ventana, y la vista solo escucha.
/// </para>
/// </summary>
public sealed class ShellViewModel
{
    private ClientSurface _current = ClientSurface.Conexion;

    /// <summary>
    /// The surface that is in front right now.
    /// </summary>
    public ClientSurface Current => _current;

    /// <summary>
    /// Raised when the player navigates to another surface (AC1).
    /// </summary>
    public event EventHandler? Navigated;

    /// <summary>
    /// Moves to a surface and announces it (AC1).
    /// </summary>
    /// <param name="surface">The surface to show.</param>
    public void Navigate(ClientSurface surface)
    {
        if (_current == surface)
        {
            return;
        }

        _current = surface;

        Navigated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The surface that a keyboard shortcut jumps to, or <c>null</c> when the key is not
    /// a navigation shortcut (AC3, UX-DR-11).
    ///
    /// <para>
    /// `Ctrl+1`…`Ctrl+4` saltan a Catálogo, Descargas, Biblioteca y Configuración, y
    /// `Ctrl+,` abre Configuración y diagnóstico.
    /// </para>
    /// </summary>
    /// <param name="key">The key that was pressed.</param>
    /// <param name="modifiers">The modifiers that were held.</param>
    /// <returns>The surface the shortcut jumps to, or <c>null</c>.</returns>
    public static ClientSurface? SurfaceForShortcut(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.Control)
        {
            return null;
        }

        return key switch
        {
            Key.D1 => ClientSurface.Catalogo,
            Key.D2 => ClientSurface.Descargas,
            Key.D3 => ClientSurface.Biblioteca,
            Key.D4 => ClientSurface.Configuracion,
            Key.OemComma => ClientSurface.Configuracion,
            _ => null,
        };
    }
}
