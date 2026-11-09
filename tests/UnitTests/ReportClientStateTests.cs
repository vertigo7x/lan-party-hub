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

        var useCase = new ReportClientState(repository, NullLogger<ReportClientState>.Instance);

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

        var useCase = new ReportClientState(repository, NullLogger<ReportClientState>.Instance);

        await useCase.ExecuteAsync(
            new ClientStateChanged(ClientId, ConnectionState.Disconnected, [], []),
            TestContext.Current.CancellationToken);

        // AD-30, AD-32: el alta del equipo es la operación de REGISTRO, así que un
        // estado de un cliente que no está registrado no crea ninguna fila.
        Assert.Null(repository.Saved);
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

        var useCase = new ReportClientState(repository, NullLogger<ReportClientState>.Instance);

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
}
