using LANPartyHub.Client.Application;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Client.Domain.Trust;
using LANPartyHub.Client.Host;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The publication of the state of the client when its LOCAL data is NOT available
/// (AD-22).
///
/// <para>
/// La publicación del estado es una INSTANTÁNEA COMPLETA: el servidor sustituye la
/// proyección que tenía por la que recibe, así que una instantánea VACÍA vacía por
/// error la vista del organizador (AD-22).
/// </para>
/// <para>
/// Un cliente SIN store local, o con una lectura local que falla, NO puede sostener una
/// instantánea completa, así que NO publica ninguna: la proyección que el servidor ya
/// tiene se CONSERVA (AD-22).
/// </para>
/// </summary>
public sealed class ClientStatePublicationWithoutLocalDataTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";

    /// <summary>
    /// A client WITHOUT a local store does NOT publish an EMPTY state: the operations
    /// that the server already projected survive (AD-22).
    ///
    /// <para>
    /// Sin store local el cliente no tiene una instantánea completa que pueda sostener,
    /// y publicarla vacía borraría las operaciones en curso del servidor (AD-22).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_client_without_a_local_store_does_not_publish_an_empty_state()
    {
        var publisher = new RecordingStatePublisher();
        var projection = new ClientOperationsProjection();

        // El servidor YA tiene una operación en curso de este equipo.
        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "download",
                    nameof(DownloadState.Downloading))
            ]);

        using var composition = new ClientRegistrationComposition(
            new Uri("http://localhost:5000"),
            new FakeRequestHeaders(),
            ClientIdentity.FromCanonical(ClientId),
            new FakeNameStore(),
            publisher,
            NullLoggerFactory.Instance,
            stateStore: null);

        await composition.PublishStateAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        // AD-22: NO se publica una instantánea vacía, porque vaciaría la proyección.
        Assert.Empty(publisher.States);

        // AD-22: la proyección que el servidor tenía NO se vacía por error.
        Assert.Single(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// A client whose LOCAL read FAILS does NOT publish an EMPTY state: the operations
    /// that the server already projected survive (AD-22).
    ///
    /// <para>
    /// Un fallo transitorio de la base de datos local NO se convierte en una instantánea
    /// vacía: la proyección del servidor se conserva y el cliente sigue operando
    /// (AD-1, AD-22).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_client_whose_local_read_fails_does_not_publish_an_empty_state()
    {
        var publisher = new RecordingStatePublisher();
        var projection = new ClientOperationsProjection();

        // El servidor YA tiene una operación en curso de este equipo.
        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "download",
                    nameof(DownloadState.Downloading))
            ]);

        using var composition = new ClientRegistrationComposition(
            new Uri("http://localhost:5000"),
            new FakeRequestHeaders(),
            ClientIdentity.FromCanonical(ClientId),
            new FakeNameStore(),
            publisher,
            NullLoggerFactory.Instance,
            stateStore: new FailingClientStateStore());

        await composition.PublishStateAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        // AD-22: NO se publica una instantánea vacía, porque vaciaría la proyección.
        Assert.Empty(publisher.States);

        // AD-22: la proyección que el servidor tenía NO se vacía por error.
        Assert.Single(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// The state that the server projects from the client that DOES have its local data
    /// is NOT erased when a later publication cannot read it: the REAL snapshot replaces
    /// the projection, y la que falla NO lo hace (AD-22).
    /// </summary>
    [Fact]
    public async Task A_failing_local_read_does_not_erase_the_projection_of_the_server()
    {
        var publisher = new RecordingStatePublisher();
        var projection = new ClientOperationsProjection();
        var registry = new RecordingRegistry();
        var installations = new RecordingInstallations();

        var report = new ReportClientState(
            registry,
            installations,
            projection,
            NullLogger<ReportClientState>.Instance);

        // El equipo se registra y publica su estado REAL.
        await report.ExecuteAsync(
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(
                        GameId,
                        "1.2.0",
                        InstallationState.Installed)
                ],
                [
                    new ClientStateChanged.ActiveOperation(
                        OperationId,
                        "download",
                        nameof(DownloadState.Downloading))
                ]),
            TestContext.Current.CancellationToken);

        Assert.Single(projection.ListActiveOperations(ClientId));

        // El cliente pierde el acceso a sus datos locales y publica otra vez.
        using var composition = new ClientRegistrationComposition(
            new Uri("http://localhost:5000"),
            new FakeRequestHeaders(),
            ClientIdentity.FromCanonical(ClientId),
            new FakeNameStore(),
            publisher,
            NullLoggerFactory.Instance,
            stateStore: new FailingClientStateStore());

        await composition.PublishStateAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        // AD-22: la publicación que no puede leer los datos locales NO viaja, así que la
        // proyección del servidor se conserva.
        Assert.Empty(publisher.States);
        Assert.Single(projection.ListActiveOperations(ClientId));

        // AD-22: las instalaciones que el servidor ya proyectó se conservan.
        Assert.Single(installations.Rows);
    }

    /// <summary>
    /// Publisher of the state of the client que RECUERDA lo que se le publicó (AD-16).
    /// </summary>
    private sealed class RecordingStatePublisher : IClientStatePublisher
    {
        public List<ClientStateChanged> States { get; } = [];

        public Task PublishAsync(ClientStateChanged state, CancellationToken cancellationToken)
        {
            States.Add(state);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Registry of the clients que SIEMPRE tiene al equipo registrado (AD-16).
    /// </summary>
    private sealed class RecordingRegistry : IClientRegistrationRepository
    {
        public Task<ClientRegistrationRow?> FindByClientIdAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientRegistrationRow?>(
                new ClientRegistrationRow(
                    ClientId,
                    "EQ-PC",
                    "1.0.0",
                    2,
                    ConnectionState.AnchorVerified,
                    DateTimeOffset.UtcNow));

        public Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
            string canonicalMachineName,
            string excludedClientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientRegistrationRow?>(null);

        public Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientRegistrationRow>>([]);

        public Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Projection of the installations that keeps the rows that el caso de uso escribe
    /// (AD-22).
    /// </summary>
    private sealed class RecordingInstallations : IClientInstallationViewRepository
    {
        private readonly List<ClientInstallationViewRow> _rows = [];

        public IReadOnlyList<ClientInstallationViewRow> Rows => _rows;

        public Task SaveAsync(ClientInstallationViewRow row, CancellationToken cancellationToken)
        {
            _rows.RemoveAll(candidate =>
                candidate.ClientId == row.ClientId && candidate.GameId == row.GameId);
            _rows.Add(row);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClientInstallationViewRow>> ListByClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientInstallationViewRow>>(
                _rows.Where(row => row.ClientId == clientId).ToArray());

        public Task RemoveAbsentAsync(
            string clientId,
            IReadOnlySet<string> reportedGameIds,
            CancellationToken cancellationToken)
        {
            _rows.RemoveAll(row =>
                row.ClientId == clientId && !reportedGameIds.Contains(row.GameId));

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Database of the client whose READ FAILS: el store local no está disponible y la
    /// lectura de las instalaciones lanza el fallo (AD-16).
    /// </summary>
    private sealed class FailingClientStateStore : IClientStateStore
    {
        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClientIdentity?>(null);

        public Task SaveIdentityAsync(ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("La base de datos local no está disponible.");

        public Task SaveProgressAsync(
            string operationId,
            long bytesPersisted,
            long? totalBytes,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveOperationAsync(
            string operationId,
            string packageId,
            string state,
            string transferEngine,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PinnedTrustAnchor?>(null);

        public Task SaveTrustAnchorAsync(PinnedTrustAnchor anchor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveInstallationAsync(ClientInstallation installation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ClientInstallation?> LoadInstallationAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientInstallation?>(null);

        public Task<IReadOnlyList<ClientInstallation>> LoadInstallationsAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("La base de datos local no está disponible.");

        public Task SaveInstallationFailureAsync(
            ClientInstallation installation,
            string errorCode,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> LoadInstallationFailureAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Headers of the requests of the cliente que SIEMPRE responden lo mismo (AD-16).
    /// </summary>
    private sealed class FakeRequestHeaders : IClientRequestHeaders
    {
        public string? ClientId => LANPartyHub.UnitTests.ClientStatePublicationWithoutLocalDataTests.ClientId;

        public string? OperationId => null;

        public string? MachineName => "EQ-PC";
    }

    /// <summary>
    /// Almacén del nombre efectivo que SIEMPRE responde lo mismo (AD-16).
    /// </summary>
    private sealed class FakeNameStore : IClientEffectiveNameStore
    {
        public Task<string?> LoadEffectiveMachineNameAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>("EQ-PC");

        public Task SaveEffectiveMachineNameAsync(
            string machineName,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
