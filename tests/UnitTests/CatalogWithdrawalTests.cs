using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Server.Domain.Catalog;
using LANPartyHub.Server.Domain.Packages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The invariants of the withdrawal of a videogame from the catalog (AC2, AC3, AD-26).
///
/// <para>
/// La retirada MARCA y no borra: la instalación que un cliente ya hizo no se invalida, y
/// la fila del paquete sobrevive para que el juego pueda volver al catálogo con una
/// versión nueva (AC2, AD-26). El caso de uso real se ejerce aquí, con dobles de los
/// puertos y no con una copia de su regla (AD-1).
/// </para>
/// </summary>
public sealed class CatalogWithdrawalTests
{
    /// <summary>
    /// Identifier of the videogame that is withdrawn. It is the canonical form of an
    /// identifier of the catalog (AD-23).
    /// </summary>
    private const string GameId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e40";

    /// <summary>Identifier of a published package of the videogame (AD-7).</summary>
    private const string PackageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e41";

    /// <summary>
    /// The withdrawal marks the published packages of the videogame and it grows the
    /// version of the catalog (AC2, AC3).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_marks_the_packages_and_grows_the_version()
    {
        var packages = new RecordingPackageRepository();
        packages.Add(new PackageDescriptorRow(
            PackageId,
            GameId,
            "1.0.0",
            1_024,
            null,
            new string('a', 64),
            "manifiesto"));

        var outcome = await WithdrawAsync(packages);

        Assert.Equal(GameId, outcome.GameId);
        Assert.Equal(1, outcome.WithdrawnPackages);
        Assert.Equal(1, outcome.CatalogVersion);

        // La retirada marca: la fila del paquete NO se borra (AC2, AD-26).
        Assert.Single(packages.WithdrawnGameIds);
        Assert.Equal(GameId, packages.WithdrawnGameIds[0]);
    }

    /// <summary>
    /// The withdrawal marks and never deletes: the descriptor of the published package
    /// survives the withdrawal (AC2, AD-26).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_does_not_delete_any_row()
    {
        var packages = new RecordingPackageRepository();

        packages.Add(new PackageDescriptorRow(
            PackageId,
            GameId,
            "1.0.0",
            1_024,
            null,
            new string('a', 64),
            "manifiesto"));

        var rowsBefore = packages.AddedDescriptors.Count;

        await WithdrawAsync(packages);

        Assert.Equal(rowsBefore, packages.AddedDescriptors.Count);
        Assert.Equal(0, packages.DeletedRows);
    }

    /// <summary>
    /// The mark of the withdrawal is an instant in UTC ISO-8601, the same convention that
    /// the rest of the persisted instants of the server follow.
    /// </summary>
    [Fact]
    public async Task The_withdrawal_marks_the_instant_of_the_withdrawal()
    {
        var packages = new RecordingPackageRepository();

        var outcome = await WithdrawAsync(packages);

        Assert.False(string.IsNullOrWhiteSpace(outcome.WithdrawnAt));
        Assert.EndsWith("Z", outcome.WithdrawnAt, StringComparison.Ordinal);
        Assert.Equal(packages.LastWithdrawnAt, outcome.WithdrawnAt);
    }

    /// <summary>
    /// A withdrawal of a videogame that does not exist is rejected with the typed
    /// exception of the domain, and nothing is written: no mark and no growth of the
    /// version (AD-1, AD-25).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_of_a_videogame_that_does_not_exist_is_rejected()
    {
        var packages = new RecordingPackageRepository();
        var games = new StubGameRepository(holdsGame: false);

        var useCase = new WithdrawGame(
            games,
            packages,
            new RecordingCatalogChangeNotifier(),
            NullLogger<WithdrawGame>.Instance,
            new FixedTimeProvider());

        var exception = await Assert.ThrowsAsync<GameWithdrawalNotFoundException>(
            () => useCase.ExecuteAsync(GameId, TestContext.Current.CancellationToken));

        Assert.Equal(GameId, exception.GameId);

        // El rechazo ocurre ANTES de cualquier escritura: ni marca ni versión (AD-1).
        Assert.Empty(packages.WithdrawnGameIds);
        Assert.Equal(0, await packages.GetCatalogVersionAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The exception of the withdrawal names the videogame that was not found, so the
    /// organizer knows which one is missing (AD-25).
    /// </summary>
    [Fact]
    public async Task The_rejection_of_the_withdrawal_names_the_videogame()
    {
        var games = new StubGameRepository(holdsGame: false);

        var useCase = new WithdrawGame(
            games,
            new RecordingPackageRepository(),
            new RecordingCatalogChangeNotifier(),
            NullLogger<WithdrawGame>.Instance,
            new FixedTimeProvider());

        var exception = await Assert.ThrowsAsync<GameWithdrawalNotFoundException>(
            () => useCase.ExecuteAsync(GameId, TestContext.Current.CancellationToken));

        Assert.Contains(GameId, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The videogame can be published again after the withdrawal: the withdrawal marks the
    /// packages, it does not close the videogame for ever (AC2, AD-26).
    /// </summary>
    [Fact]
    public async Task The_withdrawal_does_not_close_the_videogame_for_ever()
    {
        var packages = new RecordingPackageRepository();
        var games = new StubGameRepository(holdsGame: true);

        var useCase = new WithdrawGame(
            games,
            packages,
            new RecordingCatalogChangeNotifier(),
            NullLogger<WithdrawGame>.Instance,
            new FixedTimeProvider());

        await useCase.ExecuteAsync(GameId, TestContext.Current.CancellationToken);

        // El videogame sigue en el catálogo: lo que se retiró fueron sus paquetes.
        Assert.NotNull(await games.FindAsync(GameId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Withdraws a videogame through the REAL use case, with doubles of the ports (AD-1).
    /// </summary>
    /// <param name="packages">Repository that records what the use case writes.</param>
    /// <returns>The outcome of the withdrawal.</returns>
    private static Task<WithdrawalOutcome> WithdrawAsync(RecordingPackageRepository packages)
    {
        var useCase = new WithdrawGame(
            new StubGameRepository(holdsGame: true),
            packages,
            new RecordingCatalogChangeNotifier(),
            NullLogger<WithdrawGame>.Instance,
            new FixedTimeProvider());

        return useCase.ExecuteAsync(GameId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Repository of videogames that holds one, o ninguno (AD-1).
    /// </summary>
    /// <param name="holdsGame">Whether the repository holds the videogame.</param>
    private sealed class StubGameRepository : IGameRepository
    {
        private readonly GameRegistration? _game;

        public StubGameRepository(bool holdsGame)
        {
            _game = holdsGame
                ? GameRegistration.FromPersisted(
                    GameId,
                    "Counter-Strike 2",
                    "Disparadores en primera persona.",
                    ["Shooter"],
                    ["fps"],
                    "cover.png",
                    ["captura-1.png"],
                    "1.41.3",
                    7_600_000_000,
                    1,
                    10,
                    "Windows",
                    "Servidor",
                    "Portable",
                    "bin\\cs2.exe",
                    ["-novid"],
                    "game\\bin",
                    ["DirectX 12"],
                    "Propietaria",
                    "Solo para el evento")
                : null;
        }

        public Task AddAsync(GameRegistration game, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<GameRegistration?> FindAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult(
                _game is not null && string.Equals(gameId, GameId, StringComparison.Ordinal)
                    ? _game
                    : null);

        public Task<IReadOnlyList<GameRegistration>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameRegistration>>(_game is null ? [] : [_game]);

        public Task<bool> UpdateAsync(GameRegistration game, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// Repository of packages that records what the use case writes, para que la
    /// prueba pueda observar que la retirada marca y no borra (AC2).
    /// </summary>
    private sealed class RecordingPackageRepository : IPackageRepository
    {
        public List<PackageDescriptorRow> AddedDescriptors { get; } = [];

        public List<string> WithdrawnGameIds { get; } = [];

        public int DeletedRows { get; private set; }

        public string? LastWithdrawnAt { get; private set; }

        public long CatalogVersion { get; private set; }

        public void Add(PackageDescriptorRow descriptor) => AddedDescriptors.Add(descriptor);

        public Task AddAsync(
            PackageDescriptorRow descriptor,
            SignedManifestRow manifest,
            CancellationToken cancellationToken)
        {
            AddedDescriptors.Add(descriptor);
            CatalogVersion += 1;

            return Task.CompletedTask;
        }

        public Task<PackageDescriptorRow?> FindAsync(string packageId, CancellationToken cancellationToken) =>
            Task.FromResult<PackageDescriptorRow?>(null);

        public Task<IReadOnlyList<PackageDescriptorRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PackageDescriptorRow>>(AddedDescriptors);

        public Task<IReadOnlyList<PublishedPackageRow>> ListPublishedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublishedPackageRow>>([]);

        public Task<WithdrawalResult> WithdrawGameAsync(
            string gameId,
            string withdrawnAt,
            CancellationToken cancellationToken)
        {
            WithdrawnGameIds.Add(gameId);
            LastWithdrawnAt = withdrawnAt;

            var marked = AddedDescriptors.Count(
                descriptor => string.Equals(descriptor.GameId, gameId, StringComparison.Ordinal));

            CatalogVersion += 1;

            return Task.FromResult(new WithdrawalResult(marked, CatalogVersion));
        }

        public Task<long> GetCatalogVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CatalogVersion);
    }

    /// <summary>
    /// Time provider with a known instant, para que la marca de la retirada sea
    /// comprobable sin depender del reloj de la máquina.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 10, 29, 19, 50, 23, TimeSpan.Zero);
    }
}
