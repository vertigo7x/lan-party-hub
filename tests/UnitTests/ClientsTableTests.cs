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

    [AvaloniaFact]
    public void The_row_carries_the_text_of_the_terminal_results_of_the_client()
    {
        var viewModel = new ClientsViewModel(
        [
            Client(
                "EQ-PC",
                FirstClientId,
                recentOutcomes:
                [
                    new ClientRegistrationView.OperationOutcomeResult(
                        "op-2",
                        StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)),
                ]),
        ]);

        var row = Assert.Single(viewModel.Rows);

        // AD-25, AD-33: el resultado terminal se muestra con el texto de la ÚNICA tabla
        // de microcopia de Shared, y «Paquete retirado del catálogo» es un RESULTADO y
        // no un fallo.
        Assert.Equal(
            $"op-2 · {StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)}",
            row.RecentOutcomesText);
        Assert.Contains(
            StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn),
            row.RecentOutcomesText,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_table_shows_the_real_games_bytes_and_installation_phase()
    {
        var window = await OpenWindowAsync(
            Client(
                "EQ-PC",
                FirstClientId,
                installedGames:
                [
                    new ClientRegistrationView.InstalledGame(
                        "game-1",
                        "1.2.3",
                        StateDisplayMap.GetText(InstallationState.Installed)),
                ],
                activeOperations:
                [
                    new ClientRegistrationView.ActiveOperation(
                        "download-1",
                        "download",
                        StateDisplayMap.GetText(DownloadState.Downloading),
                        512,
                        2048),
                    new ClientRegistrationView.ActiveOperation(
                        "installation-1",
                        "installation",
                        StateDisplayMap.GetText(InstallationState.Installing),
                        Phase: "Extracting"),
                ]));

        var row = Assert.Single(window.ViewModel.Rows);

        // AD-22, FR-10: las tres celdas principales muestran los datos REALES que el
        // servidor proyectó, no valores reservados.
        Assert.Equal(
            $"game-1 · 1.2.3 · {StateDisplayMap.GetText(InstallationState.Installed)}",
            row.InstalledGamesText);
        Assert.Contains("512 B", row.ActiveDownloadsText, StringComparison.Ordinal);
        Assert.Contains("2048 B", row.ActiveDownloadsText, StringComparison.Ordinal);
        Assert.Contains("download-1", row.ActiveDownloadsText, StringComparison.Ordinal);
        Assert.Contains("installation-1", row.InstallationProgressText, StringComparison.Ordinal);
        Assert.Contains("Extracting", row.InstallationProgressText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_detail_of_the_row_shows_the_terminal_result_of_a_withdrawn_package()
    {
        var window = await OpenWindowAsync(
            Client(
                "EQ-PC",
                FirstClientId,
                recentOutcomes:
                [
                    new ClientRegistrationView.OperationOutcomeResult(
                        "op-2",
                        StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)),
                ]));

        window.Select(window.ViewModel.Rows[0]);

        var detail = DetailTextOf(window);

        // AD-33: el resultado «Paquete retirado del catálogo» se MUESTRA, y no se
        // presenta como un error ni como un fallo.
        Assert.Contains(StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn), detail, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "errores registrados: " + StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn),
            detail,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_detail_of_the_row_shows_the_cause_and_the_next_step_of_a_registered_error()
    {
        var error = ErrorCatalog.Resolve(ErrorCatalog.PackageIntegrityMismatchCode);

        var window = await OpenWindowAsync(
            Client(
                "EQ-PC",
                FirstClientId,
                errors:
                [
                    new ClientRegistrationView.RegisteredError(
                        "err-1",
                        "op-1",
                        ErrorCatalog.PackageIntegrityMismatchCode,
                        error.Message,
                        error.Cause,
                        error.NextStep,
                        new DateTimeOffset(2026, 11, 9, 10, 5, 0, TimeSpan.Zero)),
                ]));

        window.Select(window.ViewModel.Rows[0]);

        var detail = DetailTextOf(window);

        // AD-15, UX-DR-15: el detalle de la fila lleva la CAUSA y el SIGUIENTE PASO del
        // error registrado, no solo el código y el mensaje.
        Assert.Contains(error.Cause, detail, StringComparison.Ordinal);
        Assert.Contains(error.NextStep, detail, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_table_does_not_grow_a_column_for_the_terminal_results()
    {
        var window = await OpenWindowAsync(
            Client(
                "EQ-PC",
                FirstClientId,
                recentOutcomes:
                [
                    new ClientRegistrationView.OperationOutcomeResult(
                        "op-2",
                        StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)),
                ]));

        var rows = RowsOf(window);

        // La tabla es DENSA y su ancho es FIJO: los resultados terminales y el detalle de
        // los errores van en el DETALLE de la fila, así que la tabla NO crece una
        // columna ni pierde su altura (UX-DR-14).
        Assert.Equal(10, ClientsWindow.ColumnHeaders.Length);
        Assert.All(rows, row => Assert.Equal(40, row.Height));
        Assert.All(
            rows,
            row => Assert.Equal(10, ((Grid)row.Child!).Children.OfType<TextBlock>().Count()));
    }

    /// <summary>
    /// Builds the window of the section with the clients that the test gives it.
    ///
    /// <para>
    /// Los resultados TERMINALES se siembran en la proyección REAL de operaciones, que
    /// es donde el servidor los guarda y de donde la lectura los saca: así la prueba
    /// recorre el camino entero —proyección, lectura, contrato, fila y detalle— sin
    /// inventarse un atajo (AD-22, AD-33).
    /// </para>
    /// </summary>
    /// <param name="clients">Clients that the server has registered.</param>
    /// <returns>The window, wired to the use case that reads the clients.</returns>
    private static ClientsWindow BuildWindow(params ClientRegistrationView[] clients)
    {
        var operations = new ClientOperationsProjection();

        foreach (var client in clients)
        {
            foreach (var operation in client.ActiveOperations)
            {
                if (operation.OperationType == "download")
                {
                    operations.ReportTransfer(client.ClientId, operation.OperationId,
                        operation.BytesTransferred ?? 0, operation.TotalBytes ?? 0,
                        DownloadState.Downloading);
                }
                else if (operation.OperationType == "installation" && operation.Phase is not null)
                {
                    operations.ReportPhase(client.ClientId, operation.OperationId,
                        operation.Phase, InstallationState.Installing);
                }
            }

            foreach (var outcome in client.RecentOutcomes)
            {
                // AD-10, AD-25: el texto del resultado lo copia la tabla única de
                // microcopia, así que la prueba comprueba el texto REAL que la vista ve.
                var text = ResolveOutcome(outcome.OutcomeText);

                operations.RecordOutcome(client.ClientId, outcome.OperationId, text);
            }
        }

        return new ClientsWindow(
            new ClientsViewModel(clients),
            new ListClients(
                new FixedRegistry(clients),
                new FixedInstallations(clients),
                new FixedErrors(clients),
                operations,
                NullLogger<ListClients>.Instance));
    }

    /// <summary>
    /// Resolves the text of a terminal result back into the value of the closed
    /// enumeration of <c>OperationOutcome</c>, para que la proyección lo copie de la
    /// tabla única de microcopia (AD-25, AD-33).
    /// </summary>
    /// <param name="text">Text of the result that the client reported.</param>
    /// <returns>The outcome whose text matches.</returns>
    private static OperationOutcome ResolveOutcome(string text)
    {
        foreach (var outcome in Enum.GetValues<OperationOutcome>())
        {
            if (string.Equals(StateDisplayMap.GetText(outcome), text, StringComparison.Ordinal))
            {
                return outcome;
            }
        }

        throw new InvalidOperationException($"El texto '{text}' no es un resultado terminal conocido.");
    }

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

    /// <summary>
    /// The text of the DETALLE de la fila seleccionada, tal y como lo ve el organizador.
    /// </summary>
    /// <param name="window">Window of the section.</param>
    /// <returns>The text of the detail.</returns>
    private static string DetailTextOf(ClientsWindow window) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == ClientsWindow.DetailName)
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single()
            .Text;

    private static ClientRegistrationView Client(
        string machineName,
        string clientId,
        IReadOnlyList<ClientRegistrationView.OperationOutcomeResult>? recentOutcomes = null,
        IReadOnlyList<ClientRegistrationView.RegisteredError>? errors = null,
        IReadOnlyList<ClientRegistrationView.InstalledGame>? installedGames = null,
        IReadOnlyList<ClientRegistrationView.ActiveOperation>? activeOperations = null) =>
        new(
            clientId,
            machineName,
            "1.0.0",
            1,
            ConnectionState.AnchorVerified,
            new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero),
            installedGames: installedGames,
            activeOperations: activeOperations,
            recentOutcomes: recentOutcomes,
            errors: errors);

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

    /// <summary>
    /// A projection of the installations supplied by the test, without a database
    /// (AD-22).
    /// </summary>
    private sealed class FixedInstallations : IClientInstallationViewRepository
    {
        private readonly IReadOnlyList<ClientRegistrationView> _clients;

        public FixedInstallations(IReadOnlyList<ClientRegistrationView> clients) => _clients = clients;

        public Task SaveAsync(ClientInstallationViewRow row, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ClientInstallationViewRow>> ListByClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientInstallationViewRow>>(
                _clients.Where(client => client.ClientId == clientId)
                    .SelectMany(client => client.InstalledGames.Select(game =>
                        new ClientInstallationViewRow(clientId, game.GameId,
                            game.InstalledVersion, game.ValidationStateText)))
                    .ToArray());

        public Task RemoveAbsentAsync(
            string clientId,
            IReadOnlySet<string> reportedGameIds,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// A registry of errors that holds the errors that the test gives it, indexed by
    /// client, without a database (AD-15, AD-22).
    /// </summary>
    private sealed class FixedErrors : IClientErrorRepository
    {
        private readonly Dictionary<string, List<ClientErrorRow>> _rowsByClient =
            new(StringComparer.Ordinal);

        public FixedErrors(IEnumerable<ClientRegistrationView> clients)
        {
            foreach (var client in clients)
            {
                if (!_rowsByClient.TryGetValue(client.ClientId, out var rows))
                {
                    rows = [];
                    _rowsByClient[client.ClientId] = rows;
                }

                foreach (var error in client.Errors)
                {
                    rows.Add(new ClientErrorRow(
                        error.ErrorId,
                        error.OperationId,
                        error.Code,
                        error.Message,
                        error.Cause,
                        error.NextStep,
                        error.OccurredAt));
                }
            }
        }

        public Task<IReadOnlyList<ClientErrorRow>> ListByClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientErrorRow>>(
                _rowsByClient.TryGetValue(clientId, out var rows)
                    ? [.. rows]
                    : []);
    }
}
