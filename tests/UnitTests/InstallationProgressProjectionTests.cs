using LANPartyHub.Client.Application.Install;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Server.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The REAL state and the REAL phase of an installation operation, as the organizer
/// sees them (AD-10, AD-22, AD-25, FR-10).
///
/// <para>
/// El progreso de la instalación lleva el ESTADO REAL de la máquina y la FASE REAL de la
/// operación, así que la proyección usa el texto del estado de la tabla ÚNICA de
/// microcopia y conserva la fase REAL: una instalación que ya se está instalando NO se
/// muestra como «Pendiente» (AD-10, AD-25).
/// </para>
/// </summary>
public sealed class InstallationProgressProjectionTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";

    /// <summary>
    /// The progress that the client declares carries its REAL state, así que la
    /// proyección muestra el texto de ese estado y no el de «Pendiente» (AD-10, AD-22).
    /// </summary>
    [Fact]
    public async Task The_declared_state_is_projected_with_its_microcopy_and_the_real_phase()
    {
        var projection = new ClientOperationsProjection();
        var useCase = new ReportClientProgress(
            new RegisteredRegistry(),
            projection,
            NullLogger<ReportClientProgress>.Instance);

        await useCase.ExecuteAsync(
            new InstallationProgressUpdated(
                ClientId,
                OperationId,
                InstallationState.Installing,
                nameof(InstallPhase.Extracting)),
            TestContext.Current.CancellationToken);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        // AD-10, AD-25: el texto del estado sale de la tabla única de microcopia, así que
        // la tabla del organizador NO dice «Pendiente» de una instalación en curso.
        Assert.Equal(StateDisplayMap.GetText(InstallationState.Installing), operation.StateText);
        Assert.NotEqual(StateDisplayMap.GetText(InstallationState.Pending), operation.StateText);

        // AD-22: la fase REAL es la que declaró el cliente, tal cual.
        Assert.Equal(nameof(InstallPhase.Extracting), operation.Phase);
    }

    /// <summary>
    /// A later snapshot that replaces the operation KEEPS the REAL phase and shows the
    /// REAL state that the progress declared (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task The_snapshot_keeps_the_phase_and_the_declared_state()
    {
        var projection = new ClientOperationsProjection();
        var useCase = new ReportClientProgress(
            new RegisteredRegistry(),
            projection,
            NullLogger<ReportClientProgress>.Instance);

        await useCase.ExecuteAsync(
            new InstallationProgressUpdated(
                ClientId,
                OperationId,
                InstallationState.AwaitingConfirmation,
                nameof(InstallPhase.AwaitingConfirmation)),
            TestContext.Current.CancellationToken);

        // El cliente vuelve a declarar la lista COMPLETA de sus operaciones en curso: la
        // fase REAL NO se pierde (AD-22, FR-10).
        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    ActiveInstallations.InstallationOperationTypeName,
                    nameof(InstallationState.AwaitingConfirmation))
            ]);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(StateDisplayMap.GetText(InstallationState.AwaitingConfirmation), operation.StateText);
        Assert.Equal(nameof(InstallPhase.AwaitingConfirmation), operation.Phase);
    }

    /// <summary>
    /// A progress of a client that is NOT registered does NOT create any operation
    /// (AD-30, AD-32).
    /// </summary>
    [Fact]
    public async Task The_progress_of_an_unregistered_client_is_ignored()
    {
        var projection = new ClientOperationsProjection();
        var useCase = new ReportClientProgress(
            new RegisteredRegistry(registered: false),
            projection,
            NullLogger<ReportClientProgress>.Instance);

        await useCase.ExecuteAsync(
            new InstallationProgressUpdated(
                ClientId,
                OperationId,
                InstallationState.Installing,
                nameof(InstallPhase.Extracting)),
            TestContext.Current.CancellationToken);

        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// Registry of the clients que CONOCE al cliente de la prueba (AD-16).
    /// </summary>
    private sealed class RegisteredRegistry : IClientRegistrationRepository
    {
        private readonly ClientRegistrationRow? _existing;

        public RegisteredRegistry(bool registered = true)
        {
            _existing = registered ? new ClientRegistrationRow(
                ClientId,
                "EQ-PC",
                "1.0.0",
                2,
                ConnectionState.AnchorVerified,
                DateTimeOffset.UnixEpoch) : null;
        }

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

        public Task SaveAsync(ClientRegistrationRow row, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
