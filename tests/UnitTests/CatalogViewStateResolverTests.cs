using LANPartyHub.Client.Application.Catalog;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The derivation of the view state of a videogame of the catalog (AC3, AC4, AR-5, AR-8,
/// AD-10).
///
/// <para>
/// The derivation is the ONLY place that decides the badge of the ficha, and it is PURE:
/// it reads the snapshot that the client synchronized, the version that the server
/// publishes and the installations that the client registered.
/// </para>
/// </summary>
public sealed class CatalogViewStateResolverTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public void A_game_without_a_validated_installation_is_available()
    {
        var state = CatalogViewStateResolver.Resolve(
            CreateGame(),
            snapshotCatalogVersion: 5,
            serverCatalogVersion: 5,
            installation: null);

        Assert.Equal(CatalogViewState.Available, state);
    }

    [Fact]
    public void A_game_with_an_installation_that_is_not_validated_is_available()
    {
        // Una instalación PENDIENTE o REPARABLE no es una instalación validada: la regla de
        // la certificación es la de la Story 7.1 y esta historia NO la anticipa (AC3).
        foreach (var installationState in new[]
                 {
                     InstallationState.Pending,
                     InstallationState.Installing,
                     InstallationState.AwaitingConfirmation,
                     InstallationState.ValidatingInstallation,
                     InstallationState.Failed,
                 })
        {
            var state = CatalogViewStateResolver.Resolve(
                CreateGame(),
                snapshotCatalogVersion: 5,
                serverCatalogVersion: 5,
                installation: new InstalledGameView(GameId, "1.0.0", installationState));

            Assert.Equal(CatalogViewState.Available, state);
        }
    }

    [Fact]
    public void A_validated_installation_that_is_older_than_the_published_one_is_outdated()
    {
        var state = CatalogViewStateResolver.Resolve(
            CreateGame("1.10.0"),
            snapshotCatalogVersion: 5,
            serverCatalogVersion: 5,
            installation: new InstalledGameView(GameId, "1.9.0", InstallationState.Installed));

        Assert.Equal(CatalogViewState.InstalledOutdatedVersion, state);
    }

    [Fact]
    public void The_outdated_installation_is_derived_with_the_semantic_order_of_the_versions()
    {
        // El orden LEXICOGRÁFICO diría que «1.10.0» es anterior a «1.9.0»: con el orden
        // semántico del catálogo la instalación «1.9.0» ES la que está desactualizada
        // (AC3, AR-5).
        var state = CatalogViewStateResolver.Resolve(
            CreateGame("1.10.0"),
            snapshotCatalogVersion: 5,
            serverCatalogVersion: 5,
            installation: new InstalledGameView(GameId, "1.9.0", InstallationState.Installed));

        Assert.Equal(CatalogViewState.InstalledOutdatedVersion, state);
    }

    [Fact]
    public void A_validated_installation_that_is_up_to_date_is_not_outdated()
    {
        var state = CatalogViewStateResolver.Resolve(
            CreateGame("1.10.0"),
            snapshotCatalogVersion: 5,
            serverCatalogVersion: 5,
            installation: new InstalledGameView(GameId, "1.10.0", InstallationState.Installed));

        Assert.NotEqual(CatalogViewState.InstalledOutdatedVersion, state);
    }

    [Fact]
    public void A_client_without_a_snapshot_is_synchronizing()
    {
        var state = CatalogViewStateResolver.Resolve(
            CreateGame(),
            snapshotCatalogVersion: null,
            serverCatalogVersion: 5,
            installation: null);

        Assert.Equal(CatalogViewState.Synchronizing, state);
    }

    [Fact]
    public void A_client_with_an_outdated_snapshot_is_synchronizing()
    {
        // El snapshot del cliente es ANTERIOR a la versión que publica el servidor: el
        // cliente todavía no conoce el estado real del juego (AC3, AR-5).
        var state = CatalogViewStateResolver.Resolve(
            CreateGame(),
            snapshotCatalogVersion: 4,
            serverCatalogVersion: 5,
            installation: null);

        Assert.Equal(CatalogViewState.Synchronizing, state);
    }

    [Fact]
    public void A_client_with_an_outdated_snapshot_does_not_show_the_outdated_installation()
    {
        // El estado «sincronizando» tiene prioridad: el cliente no conoce todavía la
        // versión publicada, así que no puede afirmar que la instalación está
        // desactualizada (AC3).
        var state = CatalogViewStateResolver.Resolve(
            CreateGame("1.10.0"),
            snapshotCatalogVersion: 4,
            serverCatalogVersion: 5,
            installation: new InstalledGameView(GameId, "1.0.0", InstallationState.Installed));

        Assert.Equal(CatalogViewState.Synchronizing, state);
    }

    [Fact]
    public void A_client_that_does_not_know_the_version_of_the_server_uses_its_own_snapshot()
    {
        // Con el servidor caído el cliente conserva el último snapshot que aplicó y no
        // puede afirmar que su catálogo está desactualizado (AC3, UX-DR-32).
        var state = CatalogViewStateResolver.Resolve(
            CreateGame(),
            snapshotCatalogVersion: 5,
            serverCatalogVersion: null,
            installation: null);

        Assert.Equal(CatalogViewState.Available, state);
    }

    [Fact]
    public void A_game_that_is_not_in_the_snapshot_is_synchronizing()
    {
        var state = CatalogViewStateResolver.Resolve(
            game: null,
            snapshotCatalogVersion: 5,
            serverCatalogVersion: 5,
            installation: null);

        Assert.Equal(CatalogViewState.Synchronizing, state);
    }

    [Fact]
    public void Every_derived_state_has_its_text_in_the_single_table_of_microcopy()
    {
        // El texto de la insignia sale EXCLUSIVAMENTE de la tabla de microcopy y la vista no
        // define ninguno (AC4, AR-8, R-MVP-7).
        foreach (var state in Enum.GetValues<CatalogViewState>())
        {
            Assert.Equal(StateDisplayMap.GetText(state), StateDisplayMap.GetText(state));
            Assert.False(string.IsNullOrWhiteSpace(StateDisplayMap.GetText(state)));
        }

        Assert.Equal("sincronizando", StateDisplayMap.GetText(CatalogViewState.Synchronizing));
        Assert.Equal("disponible", StateDisplayMap.GetText(CatalogViewState.Available));
        Assert.Equal(
            "instalado con versión desactualizada",
            StateDisplayMap.GetText(CatalogViewState.InstalledOutdatedVersion));
    }

    /// <summary>
    /// Builds the contract of a videogame of the catalog.
    /// </summary>
    /// <param name="version">Version published in the catalog.</param>
    /// <returns>The contract of the videogame.</returns>
    private static GameDescriptor CreateGame(string version = "1.0.0") => new(
        GameId,
        "Counter-Strike 2",
        "Disparadores en primera persona.",
        ["Shooter"],
        ["fps"],
        "cover.png",
        [],
        version,
        7_600_000_000,
        1,
        10,
        "Windows",
        "Servidor",
        "Portable",
        new InstallationManifest.LaunchSettings("game.exe", [], "game"),
        ["DirectX 12"],
        "Propietaria",
        "Solo para el evento");
}
