using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Clients;
using LANPartyHub.Server.Presentation.Clients;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The DENSE TABLE of the clients of the event (AD-29, UX-DR-14).
///
/// <para>
/// La tabla es una TABLA y no una colección de tarjetas: una fila por cliente, de 40
/// píxeles de alto, con el orden estable por nombre de equipo y con la fila
/// seleccionable para ver su detalle (UX-DR-14).
/// </para>
/// <para>
/// La clase vive en la COLECCIÓN de las superficies de Avalonia, que NO corre en
/// paralelo: la plataforma headless tiene UN dispatcher con un hilo propio
/// (UX-DR-37).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class ClientsTableTests
{
    private const string FirstClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string SecondClientId = "0193f2a1b2c34d4e5f60718293a4b5c7";

    [AvaloniaFact]
    public async Task The_table_shows_one_row_per_client()
    {
        var window = await OpenWindowAsync(
            Client("EQ-PC", FirstClientId),
            Client("Sala-2", SecondClientId));

        var rows = RowsOf(window);

        Assert.Equal(2, rows.Count);
    }

    [AvaloniaFact]
    public async Task The_row_of_the_table_is_forty_pixels_high()
    {
        var window = await OpenWindowAsync(Client("EQ-PC", FirstClientId));

        var rows = RowsOf(window);

        // UX-DR-14: la fila de la tabla densa tiene 40 px de alto. La altura es
        // FIJA, así que el alto DECLARADO de la fila es el que la tabla mide.
        Assert.Equal(40, ClientsWindow.RowHeight);
        Assert.All(rows, row => Assert.Equal(40, row.Height));
    }

    [AvaloniaFact]
    public async Task The_order_of_the_rows_is_stable_by_team_name()
    {
        var window = await OpenWindowAsync(
            Client("Zeta-PC", SecondClientId),
            Client("Alpha-PC", FirstClientId));

        var names = NamesOf(window);

        Assert.Equal(["Alpha-PC", "Zeta-PC"], names);
    }

    [AvaloniaFact]
    public async Task The_column_of_the_name_shows_the_effective_name()
    {
        var window = await OpenWindowAsync(
            Client("EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c7", SecondClientId));

        var names = NamesOf(window);

        // AD-30: la columna muestra SIEMPRE el nombre efectivo, nunca el propuesto que
        // colisionó.
        Assert.Equal(["EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c7"], names);
    }

    [AvaloniaFact]
    public void The_table_declares_the_columns_that_the_requirement_names()
    {
        // FR-10, UX-DR-14: la tabla muestra identificador, nombre del equipo, estado de
        // conexión, juegos y versiones instalados, descargas activas, progreso de las
        // instalaciones y errores registrados.
        Assert.Equal(
            [
                "Identificador",
                "Nombre del equipo",
                "Estado de conexión",
                "Versión del cliente",
                "Versión del protocolo",
                "Último contacto",
                "Juegos instalados",
                "Descargas activas",
                "Progreso de instalaciones",
                "Errores registrados",
            ],
            ClientsWindow.ColumnHeaders);
    }

    [AvaloniaFact]
    public async Task The_state_of_the_connection_comes_from_the_table_of_microcopy()
    {
        var window = await OpenWindowAsync(Client("EQ-PC", FirstClientId));

        var states = StatesOf(window);

        // AD-25: el texto del estado sale de la tabla de microcopia de Shared, nunca de
        // uno inventado por la vista.
        Assert.Equal([StateDisplayMap.GetText(ConnectionState.AnchorVerified)], states);
    }

    [AvaloniaFact]
    public async Task The_row_is_selectable_and_shows_its_detail()
    {
        var window = await OpenWindowAsync(Client("EQ-PC", FirstClientId));

        Assert.Single(RowsOf(window));

        // La fila es SELECCIONABLE: el organizador la elige y ve su detalle sin salir
        // de la tabla (UX-DR-14).
        window.Select(window.ViewModel.Rows[0]);

        Assert.NotNull(window.SelectedRow);
        Assert.Equal("EQ-PC", window.SelectedRow!.MachineName);
    }

    [AvaloniaFact]
    public void The_view_model_projects_the_rows_ordered_by_team_name()
    {
        var viewModel = new ClientsViewModel(
        [
            Client("Zeta-PC", SecondClientId),
            Client("Alpha-PC", FirstClientId),
        ]);

        Assert.Equal(["Alpha-PC", "Zeta-PC"], viewModel.Rows.Select(row => row.MachineName));
        Assert.Equal(2, viewModel.Count);
    }

    private static ClientsWindow BuildWindow(params ClientRegistrationView[] clients) =>
        new(
            new ClientsViewModel(clients),
            new ListClients(
                new FixedRegistry(clients),
                NullLogger<ListClients>.Instance));

    /// <summary>
    /// Opens the window and reads the table, so the rows exist in the visual tree.
    /// </summary>
    /// <param name="clients">Clients that the server has registered.</param>
    /// <returns>The window, with its table already filled.</returns>
    private static async Task<ClientsWindow> OpenWindowAsync(
        params ClientRegistrationView[] clients)
    {
        var window = BuildWindow(clients);

        window.Show();

        await window.RefreshAsync();

        return window;
    }

    /// <summary>
    /// The rows of the dense table: the borders whose child is the grid of celdas y
    /// whose height is el de la fila (UX-DR-14).
    /// </summary>
    /// <param name="window">Window of the section.</param>
    /// <returns>The rows of the table.</returns>
    private static List<Border> RowsOf(ClientsWindow window) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Name == ClientsWindow.RowName)
            .ToList();

    private static string[] NamesOf(ClientsWindow window) =>
        RowsOf(window)
            .Select(border => ((Grid)border.Child!).Children.OfType<TextBlock>().ElementAt(1).Text)
            .ToArray();

    private static string[] StatesOf(ClientsWindow window) =>
        RowsOf(window)
            .Select(border => ((Grid)border.Child!).Children.OfType<TextBlock>().ElementAt(2).Text)
            .ToArray();

    private static ClientRegistrationView Client(string machineName, string clientId) =>
        new(
            clientId,
            machineName,
            "1.0.0",
            1,
            ConnectionState.AnchorVerified,
            new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// A registry that holds the rows that the test gives it, without a database.
    /// </summary>
    private sealed class FixedRegistry : IClientRegistrationRepository
    {
        private readonly List<ClientRegistrationRow> _rows = [];

        public FixedRegistry(IEnumerable<ClientRegistrationView> clients)
        {
            foreach (var client in clients)
            {
                _rows.Add(new ClientRegistrationRow(
                    client.ClientId,
                    client.MachineName,
                    client.ClientVersion,
                    client.ProtocolVersion,
                    client.ConnectionState,
                    client.LastSeenAt));
            }
        }

        public Task<ClientRegistrationRow?> FindByClientIdAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(row => row.ClientId == clientId));

        public Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
            string canonicalMachineName,
            string excludedClientId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_rows.FirstOrDefault(
                row => !string.Equals(row.ClientId, excludedClientId, StringComparison.Ordinal)
                    && string.Equals(
                        MachineNamePolicy.Canonicalize(row.MachineName),
                        canonicalMachineName,
                        StringComparison.Ordinal)));

        public Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientRegistrationRow>>(_rows);

        public Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
