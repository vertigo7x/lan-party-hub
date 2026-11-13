using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LANPartyHub.Client.Presentation.Catalog;
using LANPartyHub.Client.Presentation.Status;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The BEHAVIOUR of the interface on a laptop of the hall: it must not come apart at any
/// window size (AC1, AC2, UX-DR-42, UX-DR-4).
///
/// <para>
/// La rejilla del catálogo RECALCULA sus columnas con el ancho de la ventana, y el panel
/// de estado se REDUCE en una ventana estrecha sin desaparecer: el jugador sigue viendo
/// si está conectado y qué está pasando (AC2, UX-DR-42).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class DesktopBehaviourTests
{
    /// <summary>
    /// The GRID calculates its columns with the width of the window (AC1, UX-DR-42).
    /// </summary>
    [Fact]
    public void The_grid_calculates_its_columns_with_the_width()
    {
        // Por debajo de dos columnas cabe a lista de UNA columna (AC1).
        Assert.Equal(1, CatalogGrid.ColumnsFor(640));
        Assert.Equal(1, CatalogGrid.ColumnsFor(719));

        // Por debajo de tres columnas cabe a DOS (AC1).
        Assert.Equal(2, CatalogGrid.ColumnsFor(720));
        Assert.Equal(2, CatalogGrid.ColumnsFor(1279));

        // A 1280 px la rejilla tiene TRES columnas como minimo (AC1).
        Assert.Equal(3, CatalogGrid.ColumnsFor(1280));

        // Y HASTA SEIS en una pantalla ancha (AC1).
        Assert.Equal(6, CatalogGrid.ColumnsFor(1920));
        Assert.Equal(6, CatalogGrid.ColumnsFor(2560));
    }

    /// <summary>
    /// The cards NEVER stretch: the grid of the catalogue is a wrap panel whose cards
    /// have a fixed width (AC1, UX-DR-4).
    /// </summary>
    [AvaloniaFact]
    public void The_cards_do_not_stretch()
    {
        var grid = new CatalogGrid();
        var window = new Window { Content = grid };

        window.Show();

        var cards = window.GetVisualDescendants().OfType<WrapPanel>().Single();

        // La rejilla del catálogo es un WRAP PANEL y no una rejilla de celdas: las
        // tarjetas tienen su ancho FIJO y la ultima fila se alinea a la IZQUIERDA,
        // porque el panel las coloca de izquierda a derecha (AC1, UX-DR-4).
        Assert.Equal(Orientation.Horizontal, cards.Orientation);
        Assert.Equal(HorizontalAlignment.Stretch, cards.HorizontalAlignment);
        Assert.True(CatalogGrid.MinimumCardWidth >= 140);

        window.Close();
    }


    /// <summary>
    /// The SCALING is the one of the operating system: the interface does not multiply
    /// any size by its own factors (AC3, UX-DR-40).
    /// </summary>
    [Fact]
    public void The_interface_does_not_reimplement_the_scaling()
    {
        // El escalado lo gobierna el sistema operativo, y la interfaz no reimplanta el
        // suyo con factores propios (AC3, UX-DR-40).
        var presentation = Path.Combine(RepositoryLayout.Root, "src", "Client", "Presentation");

        var offenders = Directory
            .EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("ScaleTransform", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// No surface shows a GLOBAL network error (AC4, NFR-1, UX-DR-42).
    /// </summary>
    [Fact]
    public void No_surface_shows_a_global_network_error()
    {
        // Sin Internet, ninguna superficie muestra un error de red GLOBAL: el estado de
        // la conexion se explica en la superficie de conexion y en el panel de estado, y
        // un icono de red ausente no se confunde con «sin servidor» (AC4, UX-DR-42).
        var presentation = Path.Combine(RepositoryLayout.Root, "src", "Client", "Presentation");

        var offenders = Directory
            .EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("Error de red", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The panel of the state REDUCES in a narrow window, and it does not disappear: the
    /// presence and the operation in progress stay (AC2, UX-DR-42).
    /// </summary>
    [AvaloniaFact]
    public void The_panel_of_the_state_reduces_without_disappearing()
    {
        var panel = new ConnectionStatusPanel(new ConnectionStatusPanelViewModel());
        var window = new Window { Content = panel };

        window.Show();

        Assert.False(panel.IsCompact);

        panel.SetCompact(true);

        // El panel se REDUCE: sigue visible y la presencia sigue en el (AC2, UX-DR-42).
        Assert.True(panel.IsCompact);
        Assert.True(panel.IsVisible);
        Assert.True(panel.PresenceBadge.IsVisible);

        panel.SetCompact(false);

        Assert.False(panel.IsCompact);

        window.Close();
    }
}
