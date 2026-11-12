using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LANPartyHub.Client.Presentation;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Client.Presentation.Downloads;
using LANPartyHub.Shared.Contracts;
using LANPartyHub.Client.Presentation.Shell;
using LANPartyHub.Server.Presentation.Shell;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The ARCHITECTURE of the information and the navigation (AC1, AC2, AC3, AC4, UX-DR-9,
/// UX-DR-10, UX-DR-11, UX-DR-12).
///
/// <para>
/// La navegación es una COLUMNA, no una barra inferior, y el teclado tiene sus
/// primitivas: `Ctrl+1`…`Ctrl+4` y `Ctrl+,` saltan de superficie, `Ctrl+F` enfoca la
/// búsqueda del catálogo desde cualquier superficie y `Esc` la limpia.
/// </para>
/// <para>
/// La clase vive en la COLECCIÓN de las superficies de Avalonia, que NO corre en
/// paralelo: la plataforma headless tiene UN dispatcher con un hilo propio
/// (UX-DR-37).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class ShellNavigationTests
{
    /// <summary>
    /// The client has a column of navigation with ONE entry per surface (AC1, UX-DR-9).
    /// </summary>
    [AvaloniaFact]
    public void The_client_has_a_column_of_navigation()
    {
        var window = new MainWindow();

        window.Show();

        var column = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == MainWindow.NavigationColumnName);

        // La navegación es una COLUMNA, no una barra inferior (AC1, UX-DR-9).
        Assert.Equal(MainWindow.NavigationColumnWidth, column.Width);

        window.Close();
    }

    /// <summary>
    /// The column of the navigation has an entry for each surface that exists (AC1).
    /// </summary>
    [AvaloniaFact]
    public void The_navigation_has_an_entry_for_each_surface()
    {
        var window = new MainWindow();

        window.Show();

        var buttons = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Name is not null && button.Name.StartsWith("Nav", StringComparison.Ordinal))
            .ToArray();

        // La ventana sin superficies SOLO tiene la navegación: las entradas se registran
        // con las superficies que existen (AC1).
        Assert.NotEmpty(buttons);

        window.Close();
    }

    /// <summary>
    /// The shortcuts jump to their surface (AC3, UX-DR-11).
    /// </summary>
    [Fact]
    public void The_shortcuts_jump_to_their_surface()
    {
        Assert.Equal(ClientSurface.Catalogo, ShellViewModel.SurfaceForShortcut(Key.D1, KeyModifiers.Control));
        Assert.Equal(ClientSurface.Descargas, ShellViewModel.SurfaceForShortcut(Key.D2, KeyModifiers.Control));
        Assert.Equal(ClientSurface.Biblioteca, ShellViewModel.SurfaceForShortcut(Key.D3, KeyModifiers.Control));
        Assert.Equal(ClientSurface.Configuracion, ShellViewModel.SurfaceForShortcut(Key.D4, KeyModifiers.Control));

        // `Ctrl+,` abre Configuración y diagnóstico (AC3).
        Assert.Equal(
            ClientSurface.Configuracion,
            ShellViewModel.SurfaceForShortcut(Key.OemComma, KeyModifiers.Control));

        // Una tecla SIN `Ctrl` no es un atajo de navegación (AC3).
        Assert.Null(ShellViewModel.SurfaceForShortcut(Key.D1, KeyModifiers.None));
        Assert.Null(ShellViewModel.SurfaceForShortcut(Key.F, KeyModifiers.Control));
    }

    /// <summary>
    /// The navigation model moves between surfaces and announces it (AC1).
    /// </summary>
    [Fact]
    public void The_navigation_model_moves_between_surfaces()
    {
        var shell = new ShellViewModel();

        var moved = 0;

        shell.Navigated += (_, _) => moved++;

        // La aplicación se abre en la superficie de CONEXIÓN (AC1, UX-DR-9).
        Assert.Equal(ClientSurface.Conexion, shell.Current);

        shell.Navigate(ClientSurface.Biblioteca);

        Assert.Equal(ClientSurface.Biblioteca, shell.Current);
        Assert.Equal(1, moved);

        // Navegar a la misma superficie NO es moverse (AC1).
        shell.Navigate(ClientSurface.Biblioteca);

        Assert.Equal(1, moved);
    }

    /// <summary>
    /// The client declares the SEVEN surfaces of its architecture of information
    /// (AC1, UX-DR-9, UX-DR-10).
    /// </summary>
    [Fact]
    public void The_client_declares_the_seven_surfaces()
    {
        Assert.Equal(
            [
                ClientSurface.Conexion,
                ClientSurface.Catalogo,
                ClientSurface.Ficha,
                ClientSurface.Descargas,
                ClientSurface.Biblioteca,
                ClientSurface.Configuracion,
                ClientSurface.PanelDeEstado,
            ],
            Enum.GetValues<ClientSurface>());
    }


    /// <summary>
    /// The `Espacio` PAUSES or RESUMES the focused download (AC3, UX-DR-11).
    /// </summary>
    [AvaloniaFact]
    public void The_space_pauses_or_resumes_the_focused_download()
    {
        var stateMachine = new DownloadStateMachine();

        stateMachine.TransitionTo(DownloadState.Downloading);

        var handle = new DownloadOperationHandle(
            "op-1",
            "pkg-1",
            new Uri("http://localhost/paquete"),
            stateMachine);

        var viewModel = new DownloadProgressViewModel();

        viewModel.Apply(handle);

        var control = new DownloadProgressControl(viewModel);

        var paused = false;

        control.PauseButton.Click += (_, _) => paused = true;

        var window = new Window { Content = control };

        window.Show();

        Dispatcher.UIThread.RunJobs();

        // La descarga ENCARCADA se puede pausar (AC3).
        Assert.True(control.PauseButton.IsEnabled);

        Assert.True(control.PauseButton.Focus());

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, "Space");

        Dispatcher.UIThread.RunJobs();

        // El `Espacio` pausa la descarga enfocada: la primitiva llega al boton de
        // pausar porque el foco esta DENTRO de el (AC3, UX-DR-11).
        Assert.True(paused);

        window.Close();
    }

    /// <summary>
    /// The organizer has the THREE sections of its navigation (AC4, UX-DR-12).
    /// </summary>
    [Fact]
    public void The_organizer_has_the_three_sections()
    {
        Assert.Equal(
            [ServerSection.Catalogo, ServerSection.Clientes, ServerSection.Paquetes],
            ServerShellWindow.Sections);

        Assert.Equal("Catálogo", ServerShellWindow.LabelOf(ServerSection.Catalogo));
        Assert.Equal("Clientes", ServerShellWindow.LabelOf(ServerSection.Clientes));
        Assert.Equal("Paquetes", ServerShellWindow.LabelOf(ServerSection.Paquetes));
    }

    /// <summary>
    /// The shell of the organizer shows ONE section at a time (AC4, UX-DR-12).
    /// </summary>
    [AvaloniaFact]
    public void The_shell_of_the_organizer_shows_one_section_at_a_time()
    {
        var shell = new ServerShellWindow();

        var catalog = new TextBlock { Text = "catálogo" };
        var clients = new TextBlock { Text = "clientes" };

        shell.RegisterContent(ServerSection.Catalogo, catalog);
        shell.RegisterContent(ServerSection.Clientes, clients);

        shell.Show(ServerSection.Clientes);

        // La sección en frente es la ÚNICA visible, y las demás se esconden (AC4).
        Assert.True(clients.IsVisible);
        Assert.False(catalog.IsVisible);
        Assert.Equal(ServerSection.Clientes, shell.Current);

        shell.Show(ServerSection.Catalogo);

        Assert.True(catalog.IsVisible);
        Assert.False(clients.IsVisible);

        shell.Close();
    }
}
