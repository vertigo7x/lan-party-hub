using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The registration of the client against the event server (AD-30, AD-31, AD-32).
///
/// <para>
/// El cliente ADOPTA el nombre efectivo que el servidor le devuelve y lo PERSISTE, y
/// lo propone en las reconexiones siguientes: proponer el nombre que colisionó haría
/// que el servidor asignara un nombre derivado distinto en cada reconexión
/// (AD-30, AD-31).
/// </para>
/// </summary>
public sealed class RegisterWithServerTests
{
    [Fact]
    public async Task The_client_adopts_the_effective_name_that_the_server_returns()
    {
        var registration = new RecordingRegistrationClient(
            new ClientRegistrationOutcome(
                "0193f2a1b2c34d4e5f60718293a4b5c6",
                "EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c6",
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow,
                Renamed: true));

        var store = new RecordingNameStore();

        var useCase = new RegisterWithServer(
            registration,
            store,
            new FixedVersions(),
            NullLogger<RegisterWithServer>.Instance);

        var outcome = await useCase.ExecuteAsync(
            ClientIdentity.FromCanonical("0193f2a1b2c34d4e5f60718293a4b5c6"),
            "EQ-PC",
            TestContext.Current.CancellationToken);

        // AD-31: el nombre devuelto es SIEMPRE el efectivo, y el cliente lo adopta.
        Assert.Equal("EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c6", outcome.MachineName);
        Assert.Equal("EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c6", store.SavedName);
    }

    [Fact]
    public async Task The_client_persists_the_effective_name_only_when_it_changes()
    {
        var registration = new RecordingRegistrationClient(
            new ClientRegistrationOutcome(
                "0193f2a1b2c34d4e5f60718293a4b5c6",
                "EQ-PC",
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow,
                Renamed: false));

        var store = new RecordingNameStore { SavedName = "EQ-PC" };

        var useCase = new RegisterWithServer(
            registration,
            store,
            new FixedVersions(),
            NullLogger<RegisterWithServer>.Instance);

        await useCase.ExecuteAsync(
            ClientIdentity.FromCanonical("0193f2a1b2c34d4e5f60718293a4b5c6"),
            "EQ-PC",
            TestContext.Current.CancellationToken);

        // El nombre efectivo ya era el del servidor, así que no se escribe otra vez.
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task The_client_reads_the_effective_name_that_it_already_holds()
    {
        var registration = new RecordingRegistrationClient(
            new ClientRegistrationOutcome(
                "0193f2a1b2c34d4e5f60718293a4b5c6",
                "EQ-PC",
                ConnectionState.AnchorVerified,
                DateTimeOffset.UtcNow,
                Renamed: false));

        var store = new RecordingNameStore { SavedName = "EQ-PC" };

        var useCase = new RegisterWithServer(
            registration,
            store,
            new FixedVersions(),
            NullLogger<RegisterWithServer>.Instance);

        var effective = await useCase.LoadEffectiveNameAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("EQ-PC", effective);
    }

    private sealed class RecordingRegistrationClient : IClientRegistrationClient
    {
        private readonly ClientRegistrationOutcome _outcome;

        public RecordingRegistrationClient(ClientRegistrationOutcome outcome)
        {
            _outcome = outcome;
        }

        public string? ProposedName { get; private set; }

        public Task<ClientRegistrationOutcome> RegisterAsync(
            string clientId,
            string machineName,
            string clientVersion,
            int protocolVersion,
            CancellationToken cancellationToken)
        {
            ProposedName = machineName;

            return Task.FromResult(_outcome);
        }
    }

    private sealed class RecordingNameStore : IClientEffectiveNameStore
    {
        public string? SavedName { get; set; }

        public int SaveCount { get; private set; }

        public Task<string?> LoadEffectiveMachineNameAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SavedName);

        public Task SaveEffectiveMachineNameAsync(
            string machineName,
            CancellationToken cancellationToken)
        {
            SavedName = machineName;
            SaveCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class FixedVersions : IClientVersionProvider
    {
        public string ClientVersion => "1.0.0";

        public int ProtocolVersion => 1;
    }
}
