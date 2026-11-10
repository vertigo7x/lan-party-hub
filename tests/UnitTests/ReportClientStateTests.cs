using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The persistence of the state of the connection that a client declares
/// (AD-18, AD-22, AD-29, FR-10).
///
/// <para>
/// La transición del <c>connectionState</c> de un <c>ClientRegistration</c> es un hecho
/// INTERNO del servidor que se PERSISTE, para que la tabla de clientes refleje la
/// conexión y la desconexión de cada equipo (AD-29, FR-10).
/// </para>
/// </summary>
public sealed class ReportClientStateTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public async Task The_state_that_the_client_declares_is_persisted()
    {
        var repository = new RecordingRegistry(
            new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                1,
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow));

        var useCase = new ReportClientState(
            repository,
            new RecordingInstallations(),
            new ClientOperationsProjection(),
            NullLogger<ReportClientState>.Instance);

        await useCase.ExecuteAsync(
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        Assert.NotNull(repository.Saved);
        Assert.Equal(ConnectionState.Disconnected, repository.Saved!.ConnectionState);
    }

    [Fact]
    public async Task The_state_of_a_client_that_is_not_registered_creates_no_row()
    {
        var repository = new RecordingRegistry();

        var useCase = new ReportClientState(
            repository,
            new RecordingInstallations(),
            new ClientOperationsProjection(),
            NullLogger<ReportClientState>.Instance);

        await useCase.ExecuteAsync(
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un
        // estado de un cliente que no está registrado no crea ninguna fila.
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task An_unknown_installation_state_is_rejected_without_losing_the_other_installations()
    {
        var repository = new RecordingRegistry(
            new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                1,
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow));

        var installations = new RecordingInstallations();
        var useCase = new ReportClientState(
            repository,
            installations,
            new ClientOperationsProjection(),
            NullLogger<ReportClientState>.Instance);

        await useCase.ExecuteAsync(
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                [
                    new ClientStateChanged.InstalledGame(
                        "game-unknown",
                        "1.0.0",
                        (InstallationState)999),
                    new ClientStateChanged.InstalledGame(
                        "game-valid",
                        "2.0.0",
                        InstallationState.Installed),
                ],
                []),
            TestContext.Current.CancellationToken);

        // AD-22: un estado desconocido se rechaza SOLO en esa fila y el resto de la
        // proyección se conserva.
        var row = Assert.Single(installations.Rows);
        Assert.Equal("game-valid", row.GameId);
        Assert.Equal(
            StateDisplayMap.GetText(InstallationState.Installed),
            row.ValidationStateText);

        await useCase.ExecuteAsync(
            new ClientStateChanged(ClientId, ConnectionState.AnchorVerified,
                [new ClientStateChanged.InstalledGame("game-valid", "2.0.0", (InstallationState)999)],
                []),
            TestContext.Current.CancellationToken);

        // Rechazar el valor desconocido no borra una instalación ya proyectada.
        Assert.Equal("game-valid", Assert.Single(installations.Rows).GameId);
    }

    [Fact]
    public async Task Null_lists_in_a_malformed_snapshot_do_not_fail_the_publication()
    {
        var repository = new RecordingRegistry(
            new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                1,
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow));

        var installations = new RecordingInstallations();
        var operations = new ClientOperationsProjection();
        var useCase = new ReportClientState(
            repository,
            installations,
            operations,
            NullLogger<ReportClientState>.Instance);

        await useCase.ExecuteAsync(
            new ClientStateChanged(
                ClientId,
                ConnectionState.AnchorVerified,
                null!,
                null!),
            TestContext.Current.CancellationToken);

        Assert.NotNull(repository.Saved);
        Assert.Equal(ConnectionState.AnchorVerified, repository.Saved!.ConnectionState);
        Assert.Empty(installations.Rows);
        Assert.Empty(operations.ListActiveOperations(ClientId));
    }

    [Fact]
    public async Task The_state_does_not_change_the_effective_name_of_the_team()
    {
        var repository = new RecordingRegistry(
            new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                1,
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow));

        var useCase = new ReportClientState(
            repository,
            new RecordingInstallations(),
            new ClientOperationsProjection(),
            NullLogger<ReportClientState>.Instance);

        await useCase.ExecuteAsync(
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        // AD-30: el nombre efectivo es el único bajo el que la fila está registrada, y
        // el estado de la conexión no lo toca.
        Assert.Equal("EQ-PC", repository.Saved!.MachineName);
    }

    private sealed class RecordingRegistry : IClientRegistrationRepository
    {
        private readonly ClientRegistrationRow? _existing;

        public RecordingRegistry(ClientRegistrationRow? existing = null)
        {
            _existing = existing;
        }

        public ClientRegistrationRow? Saved { get; private set; }

        public Task<ClientRegistrationRow?> FindByClientIdAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_existing);

        public Task<ClientRegistrationRow?> FindByCanonicalMachineNameAsync(
            string canonicalMachineName,
            string excludedClientId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ClientRegistrationRow?>(null);

        public Task<IReadOnlyList<ClientRegistrationRow>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClientRegistrationRow>>(
                _existing is null ? [] : [_existing]);

        public Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken)
        {
            Saved = row;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A projection of the installations that keeps the rows that the use case writes,
    /// without a database (AD-22).
    /// </summary>
    private sealed class RecordingInstallations : IClientInstallationViewRepository
    {
        private readonly List<ClientInstallationViewRow> _rows = [];

        public IReadOnlyList<ClientInstallationViewRow> Rows => _rows;

        public Task SaveAsync(ClientInstallationViewRow row, CancellationToken cancellationToken)
        {
            // AD-22: el UPSERT es idempotente por (clientId, gameId).
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
}
