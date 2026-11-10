using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The client → server event that carries ONLY the connection state when the client
/// cannot read its complete local state (AD-18, AD-22, AD-29, AD-30, AD-32).
/// </summary>
public sealed class ClientConnectionChangedTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c7";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c8";

    [Fact]
    public async Task A_failing_local_read_publishes_only_the_connection()
    {
        var statePublisher = new RecordingStatePublisher();
        var connectionPublisher = new RecordingConnectionPublisher();

        using var composition = CreateComposition(
            statePublisher,
            connectionPublisher,
            new FailingClientStateStore());

        await composition.PublishStateAsync(
            ConnectionState.Disconnected,
            TestContext.Current.CancellationToken);

        Assert.Empty(statePublisher.States);

        var changed = Assert.Single(connectionPublisher.Changes);
        Assert.Equal(ClientId, changed.ClientId);
        Assert.Equal(ConnectionState.Disconnected, changed.ConnectionState);
    }

    [Fact]
    public async Task A_client_without_a_local_store_publishes_only_the_connection()
    {
        var statePublisher = new RecordingStatePublisher();
        var connectionPublisher = new RecordingConnectionPublisher();

        using var composition = CreateComposition(
            statePublisher,
            connectionPublisher,
            stateStore: null);

        await composition.PublishStateAsync(
            ConnectionState.AnchorVerified,
            TestContext.Current.CancellationToken);

        Assert.Empty(statePublisher.States);

        var changed = Assert.Single(connectionPublisher.Changes);
        Assert.Equal(ClientId, changed.ClientId);
        Assert.Equal(ConnectionState.AnchorVerified, changed.ConnectionState);
    }

    [Fact]
    public async Task The_connection_event_only_updates_the_connection_and_keeps_installations_and_operations()
    {
        var registry = new RecordingRegistry
        {
            Row = new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                VersionInfo.CurrentProtocolVersion,
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow),
        };
        var installations = new RecordingInstallations
        {
            Rows =
            [
                new ClientInstallationViewRow(
                    ClientId,
                    GameId,
                    "1.2.0",
                    StateDisplayMap.GetText(InstallationState.Installed)),
            ],
        };
        var operations = new ClientOperationsProjection();
        operations.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "download",
                    nameof(DownloadState.Downloading)),
            ]);

        var useCase = new ReportClientConnection(
            registry,
            NullLogger<ReportClientConnection>.Instance);

        await useCase.ExecuteAsync(
            new ClientConnectionChanged(ClientId, ConnectionState.Disconnected),
            TestContext.Current.CancellationToken);

        Assert.NotNull(registry.Saved);
        Assert.Equal(ConnectionState.Disconnected, registry.Saved.ConnectionState);
        Assert.Equal("EQ-PC", registry.Saved.MachineName);
        Assert.Equal("1.0.0", registry.Saved.ClientVersion);
        Assert.Equal(VersionInfo.CurrentProtocolVersion, registry.Saved.ProtocolVersion);
        Assert.Single(installations.Rows);
        Assert.Single(operations.ListActiveOperations(ClientId));
    }

    [Fact]
    public async Task The_connection_event_does_not_create_an_unregistered_client()
    {
        var registry = new RecordingRegistry
        {
            Row = null,
        };
        var useCase = new ReportClientConnection(
            registry,
            NullLogger<ReportClientConnection>.Instance);

        await useCase.ExecuteAsync(
            new ClientConnectionChanged(ClientId, ConnectionState.AnchorVerified),
            TestContext.Current.CancellationToken);

        Assert.Null(registry.Saved);
        Assert.Empty(registry.SavedRows);
    }

    [Fact]
    public async Task The_connection_event_keeps_the_instant_of_the_registration()
    {
        // AD-30: el `lastSeenAt` es el instante del REGISTRO del cliente. El evento de
        // conexión NO lo mueve: solo cambia el estado de conexión de la fila (AD-22,
        // AD-29).
        var registeredAt = new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);

        var registry = new RecordingRegistry
        {
            Row = new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                VersionInfo.CurrentProtocolVersion,
                ConnectionState.AnchorVerified,
                registeredAt),
        };
        var installations = new RecordingInstallations
        {
            Rows =
            [
                new ClientInstallationViewRow(
                    ClientId,
                    GameId,
                    "1.2.0",
                    StateDisplayMap.GetText(InstallationState.Installed)),
            ],
        };
        var operations = new ClientOperationsProjection();
        operations.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "download",
                    nameof(DownloadState.Downloading)),
            ]);

        var useCase = new ReportClientConnection(
            registry,
            NullLogger<ReportClientConnection>.Instance);

        await useCase.ExecuteAsync(
            new ClientConnectionChanged(ClientId, ConnectionState.Disconnected),
            TestContext.Current.CancellationToken);

        Assert.NotNull(registry.Saved);

        // El estado de conexión SÍ cambia.
        Assert.Equal(ConnectionState.Disconnected, registry.Saved.ConnectionState);

        // El instante del registro NO cambia.
        Assert.Equal(registeredAt, registry.Saved.LastSeenAt);

        // Las instalaciones y las operaciones en curso NO se tocan.
        Assert.Single(installations.Rows);
        Assert.Single(operations.ListActiveOperations(ClientId));
    }

    private static ClientRegistrationComposition CreateComposition(
        RecordingStatePublisher statePublisher,
        RecordingConnectionPublisher connectionPublisher,
        IClientStateStore? stateStore) =>
        new(
            new Uri("http://localhost:5000"),
            new FakeRequestHeaders(),
            LANPartyHub.Client.Domain.ClientIdentity.FromCanonical(ClientId),
            new FakeNameStore(),
            statePublisher,
            NullLoggerFactory.Instance,
            stateStore,
            connectionPublisher: connectionPublisher);

    private sealed class RecordingStatePublisher : IClientStatePublisher
    {
        public List<ClientStateChanged> States { get; } = [];

        public Task PublishAsync(ClientStateChanged state, CancellationToken cancellationToken)
        {
            States.Add(state);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingConnectionPublisher : IClientConnectionPublisher
    {
        public List<ClientConnectionChanged> Changes { get; } = [];

        public Task PublishAsync(ClientConnectionChanged changed, CancellationToken cancellationToken)
        {
            Changes.Add(changed);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRegistry : IClientRegistrationRepository
    {
        public ClientRegistrationRow? Row { get; init; }

        public ClientRegistrationRow? Saved { get; private set; }

        public List<ClientRegistrationRow> SavedRows { get; } = [];

        public Task<ClientRegistrationRow?> FindByClientIdAsync(
            string clientId,
            CancellationToken cancellationToken) => Task.FromResult(Row);

        public Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
            string canonicalMachineName,
            string excludedClientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientRegistrationRow?>(null);

        public Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientRegistrationRow>>([]);

        public Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken)
        {
            Saved = row;
            SavedRows.Add(row);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInstallations : IClientInstallationViewRepository
    {
        public List<ClientInstallationViewRow> Rows { get; init; } = [];

        public Task SaveAsync(ClientInstallationViewRow row, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ClientInstallationViewRow>> ListByClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientInstallationViewRow>>(Rows);

        public Task RemoveAbsentAsync(
            string clientId,
            IReadOnlySet<string> reportedGameIds,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingClientStateStore : IClientStateStore
    {
        public Task MigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LANPartyHub.Client.Domain.ClientIdentity?> LoadIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LANPartyHub.Client.Domain.ClientIdentity?>(null);

        public Task SaveIdentityAsync(LANPartyHub.Client.Domain.ClientIdentity identity, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LANPartyHub.Client.Application.PersistedDownloadOperation>> LoadDownloadOperationsAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("La base de datos local no está disponible.");

        public Task SaveProgressAsync(
            string operationId,
            long bytesPersisted,
            long? totalBytes,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveOperationAsync(
            string operationId,
            string packageId,
            string state,
            string transferEngine,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LANPartyHub.Client.Domain.Trust.PinnedTrustAnchor?> LoadTrustAnchorAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LANPartyHub.Client.Domain.Trust.PinnedTrustAnchor?>(null);

        public Task SaveTrustAnchorAsync(LANPartyHub.Client.Domain.Trust.PinnedTrustAnchor anchor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveInstallationAsync(LANPartyHub.Client.Application.ClientInstallation installation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<LANPartyHub.Client.Application.ClientInstallation?> LoadInstallationAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LANPartyHub.Client.Application.ClientInstallation?>(null);

        public Task<IReadOnlyList<LANPartyHub.Client.Application.ClientInstallation>> LoadInstallationsAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("La base de datos local no está disponible.");

        public Task SaveInstallationFailureAsync(
            LANPartyHub.Client.Application.ClientInstallation installation,
            string errorCode,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> LoadInstallationFailureAsync(
            string gameId,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeRequestHeaders : IClientRequestHeaders
    {
        public string? ClientId => ClientConnectionChangedTests.ClientId;

        public string? OperationId => null;

        public string? MachineName => "EQ-PC";
    }

    private sealed class FakeNameStore : IClientEffectiveNameStore
    {
        public Task<string?> LoadEffectiveMachineNameAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>("EQ-PC");

        public Task SaveEffectiveMachineNameAsync(string machineName, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
