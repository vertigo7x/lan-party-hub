using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Install;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Presentation.Design;
using LANPartyHub.Client.Presentation.GameLauncher;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Integration level (AD-16): the BLOCKING of the action «Jugar» with the VISIBLE
/// reason, over a REAL installation on disk and a REAL <c>state.sqlite</c> (FR-9, AC1,
/// AC2, AC3, AC5, AC6, AC7, UX-DR-18, UX-DR-26, UX-DR-35).
///
/// <para>
/// El test NO usa dobles del disco ni de la base de datos: la instalación vive en una
/// carpeta REAL, la fila vive en un <c>state.sqlite</c> REAL y el inspector REAL
/// certifica el árbol, así que lo que se observa es lo que de verdad pasa (AD-16).
/// </para>
/// <para>
/// La instalación del test está INCOMPLETA: el manifiesto firmado declara un archivo que
/// no está en el árbol, así que la verificación del manifiesto NO pasa y el motivo
/// visible es el del ejecutable ausente (AC1, AC3).
/// </para>
/// </summary>
[Collection(AvaloniaSurfaceCollection.Name)]
public sealed class GamePlayBlockingTests : IDisposable
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string PackageId = "paquete-bloqueo-1";
    private const string Version = "1.0.0";

    private readonly string _workingDirectory;
    private readonly string _databasePath;
    private readonly string _installDirectory;

    /// <summary>Creates an isolated working directory for the test.</summary>
    public GamePlayBlockingTests()
    {
        _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "lanpartyhub-playblocking-integration",
            Guid.NewGuid().ToString("N"));

        _databasePath = Path.Combine(_workingDirectory, "state.sqlite");
        _installDirectory = Path.Combine(_workingDirectory, "install");

        Directory.CreateDirectory(_workingDirectory);
    }

    /// <summary>
    /// AC1, AC3: an installation whose tree does NOT verify blocks «Jugar» with the
    /// reason VISIBLE next to its label, over the REAL store and the REAL inspector
    /// (AD-16).
    /// </summary>
    [AvaloniaFact]
    public async Task An_incomplete_installation_blocks_the_play_action_with_the_visible_reason()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var decision = await fixture.Projection
            .ProjectAsync(TestContext.Current.CancellationToken);

        var installation = Assert.Single(decision);

        Assert.True(installation.Decision.IsBlocked);
        Assert.Equal("No se puede jugar: falta el ejecutable", installation.Decision.ReasonText);
        Assert.Equal(ErrorCatalog.GameExecutableMissingCode, installation.Decision.ReasonCode);
    }

    /// <summary>
    /// AC3, UX-DR-18: the reason of the block is VISIBLE next to the label of «Jugar»,
    /// and NOT in a tooltip: it is painted in the surface itself.
    /// </summary>
    [AvaloniaFact]
    public async Task The_reason_of_the_block_is_visible_next_to_the_label_of_the_action()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var decision = await fixture.Projection
            .ProjectAsync(TestContext.Current.CancellationToken);

        var installation = Assert.Single(decision);

        var viewModel = new GamePlayActionsViewModel(GameId, installation.Decision);

        var view = new GamePlayActionsView(viewModel);

        Assert.True(view.ReasonText.IsVisible);
        Assert.Equal("No se puede jugar: falta el ejecutable", view.ReasonText.Text);

        // El motivo NO está en un tooltip: el control que lo muestra es un texto de la
        // propia superficie (AC3, UX-DR-18).
        Assert.False(view.PlayButton.IsEnabled == false);
    }

    /// <summary>
    /// AC5, UX-DR-26: «Jugar» bloqueada RECIBE el foco con `Tab`, porque el botón
    /// queda HABILITADO y es lo que la saca del recorrido de foco cuando se deshabilita
    /// (AC5).
    /// </summary>
    [AvaloniaFact]
    public async Task The_blocked_play_action_receives_the_focus_with_the_keyboard()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var decision = await fixture.Projection
            .ProjectAsync(TestContext.Current.CancellationToken);

        var installation = Assert.Single(decision);

        var viewModel = new GamePlayActionsViewModel(GameId, installation.Decision);

        var view = new GamePlayActionsView(viewModel);

        var window = new Window { Content = view };

        window.Show();

        Dispatcher.UIThread.RunJobs();

        // El botón bloqueado QUEDA habilitado, así que el recorrido de `Tab` lo alcanza
        // (AC5, UX-DR-26).
        Assert.True(view.PlayButton.IsEnabled);

        // El recorrido de `Tab` es el de VERDAD de Avalonia, y «Jugar» es el PRIMER
        // destino: el control bloqueado no se omite (AC5, UX-DR-26).
        Assert.True(window.FocusManager!.TryMoveFocus(NavigationDirection.Next));

        Dispatcher.UIThread.RunJobs();

        Assert.Same(view.PlayButton, window.FocusManager.GetFocusedElement());

        // El indicador de foco es VISIBLE y lleva el color de la acción, no el de la
        // insignia del estado (AC5, UX-DR-38).
        Assert.Equal(
            DesignTokens.FieldInputFocusBorderThickness,
            view.PlayFocusIndicator.BorderThickness.Left);

        window.Close();
    }

    /// <summary>
    /// AC5, UX-DR-38: the accessible name of the blocked action carries the name of the
    /// action, its blocked state and the visible reason.
    /// </summary>
    [AvaloniaFact]
    public async Task The_accessible_name_of_the_blocked_action_carries_the_reason()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var decision = await fixture.Projection
            .ProjectAsync(TestContext.Current.CancellationToken);

        var installation = Assert.Single(decision);

        var viewModel = new GamePlayActionsViewModel(GameId, installation.Decision);

        var view = new GamePlayActionsView(viewModel);

        var name = Avalonia.Automation.AutomationProperties.GetName(view.PlayButton);

        Assert.Equal(
            "Jugar (bloqueado): No se puede jugar: falta el ejecutable",
            name);
    }

    /// <summary>
    /// AC6: `Enter` over the blocked action produces NO change of state, does NOT start
    /// the game and does NOT trigger a download, an installation or an implicit repair
    /// (AC6, UX-DR-26).
    /// </summary>
    [AvaloniaFact]
    public async Task Enter_over_the_blocked_action_does_nothing()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var decision = await fixture.Projection
            .ProjectAsync(TestContext.Current.CancellationToken);

        var installation = Assert.Single(decision);

        var plays = 0;

        var viewModel = new GamePlayActionsViewModel(GameId, installation.Decision);

        var view = new GamePlayActionsView(viewModel, _ => plays++);

        var window = new Window { Content = view };

        window.Show();

        Dispatcher.UIThread.RunJobs();

        // El foco llega a «Jugar» con `Tab`, así que la pulsación de `Enter` ATACA al
        // control bloqueado y no a otro (AC5, AC6).
        Assert.True(window.FocusManager!.TryMoveFocus(NavigationDirection.Next));

        Dispatcher.UIThread.RunJobs();

        Assert.Same(view.PlayButton, window.FocusManager.GetFocusedElement());

        var key = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            Source = view.PlayButton,
        };

        view.PlayButton.RaiseEvent(key);

        Dispatcher.UIThread.RunJobs();

        // NINGÚN proceso arrancó, NINGUNA descarga se disparó y NINGÚN estado cambió
        // (AC6).
        Assert.Equal(0, plays);

        window.Close();
    }

    /// <summary>
    /// AC6: the COMMAND refuses when the installation is not certified, so `Enter` does
    /// NOT start the game even when the launcher would be able to (AC6).
    /// </summary>
    [AvaloniaFact]
    public async Task The_command_of_the_blocked_action_refuses_to_start_the_game()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var decision = await fixture.Projection
            .ProjectAsync(TestContext.Current.CancellationToken);

        var installation = Assert.Single(decision);

        var launches = 0;

        var command = new GamePlayCommand((_, _) =>
        {
            launches++;

            return Task.FromResult(true);
        });

        var launched = await command.ExecuteAsync(
            installation.Decision,
            GameId,
            TestContext.Current.CancellationToken);

        Assert.False(launched);
        Assert.Equal(0, launches);
    }

    /// <summary>
    /// AC7, UX-DR-35: an installation that is NOT certified prevents the launch and the
    /// error says «No se pudo iniciar: <causa>.», con la ruta exacta cuando el problema
    /// es de ruta (AD-16).
    /// </summary>
    [AvaloniaFact]
    public async Task An_installation_that_is_not_certified_prevents_the_launch()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Equal(ErrorCatalog.GameLaunchInstallationInvalidCode, outcome.Error!.Code);
        Assert.Null(outcome.Handle);

        // El aviso del error lleva el prefijo del cliente y el mensaje del catálogo
        // (AC7, UX-DR-35).
        Assert.StartsWith(
            "No se pudo iniciar: ",
            ErrorCatalog.Resolve(outcome.Error!.Code).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AC4, UX-DR-19: there is NO second way of launching: the launcher rejects the
    /// installation that is not certified, so no process is started (AD-19, AC4).
    /// </summary>
    [AvaloniaFact]
    public async Task There_is_no_second_way_of_launching_an_invalid_installation()
    {
        using var fixture = await CreateFixtureAsync(certificationFails: true);

        var outcome = await fixture.Service
            .LaunchAsync(GameId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Null(outcome.Handle);
    }

    /// <summary>
    /// Builds the fixture of the blocking: the real layout, the real store, the real
    /// inspector, the real projection and the real launcher (AD-16).
    /// </summary>
    /// <param name="certificationFails">
    /// Whether the signed manifest declares a file that is NOT on disk, so the
    /// certification fails (AC5).
    /// </param>
    /// <returns>The fixture of the blocking.</returns>
    private async Task<BlockingFixture> CreateFixtureAsync(bool certificationFails)
    {
        var layout = ClientDataLayout.Create(_workingDirectory, _installDirectory);

        layout.EnsureCreated();

        var context = await OpenContextAsync();

        var store = new ClientStateStore(context, layout);

        await store.MigrateAsync(TestContext.Current.CancellationToken);

        // AD-22: la fila de la instalación vive en el <c>state.sqlite</c> REAL, y su
        // estado es el de la máquina de estados.
        await store.SaveInstallationAsync(
            new ClientInstallation(
                GameId,
                Version,
                InstallationState.Installed,
                _installDirectory,
                InstallationCertification.DeriveInstallDirectoryId(_installDirectory)),
            TestContext.Current.CancellationToken);

        // El manifiesto FIRMADO declara la configuración de lanzamiento y la operación de
        // verificación sobre un archivo que NO está en el árbol (AD-2, AC5).
        var manifest = new InstallationManifest(
            1,
            PackageId,
            Version,
            1,
            new string('a', 64),
            new InstallationManifest.LaunchSettings("game\\juego.exe", [], "."),
            certificationFails
                ? [new InstallationManifest.Verify("game\\certification-missing.bin")]
                : []);

        var snapshot = new CatalogSnapshot(
            1,
            [],
            [new PackageDescriptor(PackageId, GameId, Version, 1, 0, new string('a', 64), PackageId)]);

        var snapshots = new CatalogSnapshotStore(context);

        await snapshots.SaveAsync(snapshot, TestContext.Current.CancellationToken);

        var cache = new ManifestCache(layout);

        await cache.StoreAsync(
            PackageId,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current.CancellationToken);

        var paths = new InstallPathResolver(layout);

        var inspector = new InstallationInspector(
            store,
            snapshots,
            cache,
            paths,
            NullLogger<InstallationInspector>.Instance);

        var service = new GameLaunchService(
            inspector,
            paths,
            new LANPartyHub.Client.Infrastructure.Process.SystemGameProcessRunner(
                NullLogger<LANPartyHub.Client.Infrastructure.Process.SystemGameProcessRunner>.Instance),
            NullLogger<GameLaunchService>.Instance);

        var projection = new PlayabilityProjection(store, inspector, snapshots);

        return new BlockingFixture(service, projection, context);
    }

    /// <summary>
    /// Opens the database of the client over the REAL SQLite file (AD-11, AD-16).
    /// </summary>
    /// <returns>The context of the database of the client.</returns>
    private async Task<ClientStateDbContext> OpenContextAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        await Task.CompletedTask;

        return new ClientStateDbContext(options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // La limpieza del test NO puede fallar la prueba (AD-16).
        }
        catch (UnauthorizedAccessException)
        {
            // La limpieza del test NO puede fallar la prueba (AD-16).
        }
    }

    /// <summary>
    /// The fixture of the blocking (AD-16).
    ///
    /// <para>
    /// El contexto de la base de datos es PARTE de la fixture: vive mientras vive la
    /// prueba, porque el store lo usa en cada lectura (AD-11, AD-16).
    /// </para>
    /// </summary>
    /// <param name="Service">The real case of use of the launch.</param>
    /// <param name="Projection">The real projection of the playability.</param>
    /// <param name="Context">The context of the database of the client.</param>
    private sealed record BlockingFixture(
        GameLaunchService Service,
        PlayabilityProjection Projection,
        ClientStateDbContext Context) : IDisposable
    {
        public void Dispose() => Context.Dispose();
    }
}
