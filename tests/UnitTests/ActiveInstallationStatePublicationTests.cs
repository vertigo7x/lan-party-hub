using LANPartyHub.Client.Application.Clients;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain;
using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The PUBLICATION of the state of the client when an installation operation starts and
/// when it finishes (AD-22, FR-10).
///
/// <para>
/// Una operación de instalación es una UNIDAD DE TRABAJO EN CURSO, así que el servidor
/// tiene que VERLA aparecer y VERLA desaparecer. La lista de operaciones en curso la
/// publica el REGISTRO del equipo, así que el tracker se ENLAZA TARDE a esa publicación:
/// el registro necesita al tracker y el tracker necesita al registro, y construirlos
/// con el uno al otro al mismo tiempo sería un ciclo (AD-22).
/// </para>
/// <para>
/// La publicación es de MEJOR ESFUERZO: un servidor que no recibe el estado no puede
/// impedir que el manifiesto se aplique (AD-1, AD-22).
/// </para>
/// </summary>
public sealed class ActiveInstallationStatePublicationTests
{
    private const string GameId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6";
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    /// <summary>
    /// The operation that BEGINS reaches the server as an operation in course, its REAL
    /// phase is published, and the operation that ENDS leaves the list (AD-22, FR-10).
    /// </summary>
    [Fact]
    public async Task The_operation_that_begins_is_declared_and_the_one_that_ends_leaves_the_list()
    {
        var tracker = new ActiveInstallations();
        var projection = new ClientOperationsProjection();
        var publisher = new RecordingStatePublisher();

        // AD-22: la publicación del estado COMPLETO se enlaza TARDE, con la publicación
        // del registro del equipo.
        tracker.ConfigureStatePublication(async cancellationToken =>
        {
            await publisher
                .PublishAsync(
                    new ClientStateChanged(
                        ClientId,
                        ConnectionState.AnchorVerified,
                        [],
                        tracker.ListActiveOperations()),
                    cancellationToken);
        });

        var operationId = tracker.Begin(ClientId, GameId);

        // La operación EN CURSO se declara al servidor al empezar (AD-22, FR-10).
        await tracker.PublishStateAsync(TestContext.Current.CancellationToken);

        var declared = Assert.Single(publisher.States.Last().ActiveOperations);
        Assert.Equal(operationId, declared.OperationId);
        Assert.Equal(ActiveInstallations.InstallationOperationTypeName, declared.OperationType);
        Assert.Equal(nameof(InstallationState.Pending), declared.State);

        // La fase REAL se publica con el identificador de la operación (AD-22).
        await tracker.ReportPhaseAsync(
            ClientId,
            operationId,
            InstallPhase.Extracting,
            TestContext.Current.CancellationToken);

        Assert.Equal(nameof(InstallPhase.Extracting), tracker.PhaseOf(operationId));

        // La operación TERMINADA deja de estar en curso (AD-22, AD-26).
        tracker.Complete(operationId);

        await tracker.PublishStateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(publisher.States.Last().ActiveOperations);
        Assert.Null(tracker.PhaseOf(operationId));
    }

    /// <summary>
    /// The REAL phase of an installation SURVIVES the snapshot that declares the list of
    /// operations in course, así que un nuevo estado completo NO borra el progreso que el
    /// cliente ya declaró (AD-22, FR-10).
    /// </summary>
    [Fact]
    public void The_real_phase_survives_the_snapshot_of_the_operations_in_course()
    {
        var tracker = new ActiveInstallations();
        var projection = new ClientOperationsProjection();

        var operationId = tracker.Begin(ClientId, GameId);

        tracker.ReportPhaseAsync(
            ClientId,
            operationId,
            InstallPhase.Extracting,
            TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();

        // El servidor recibe la fase REAL por su propio canal (AD-22).
        projection.ReportPhase(ClientId, operationId, nameof(InstallPhase.Extracting));

        // El cliente vuelve a declarar la lista COMPLETA de sus operaciones en curso: la
        // fase REAL NO se pierde (AD-22, FR-10).
        projection.ReplaceActiveOperations(
            ClientId,
            tracker.ListActiveOperations());

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(operationId, operation.OperationId);
        Assert.Equal(nameof(InstallPhase.Extracting), operation.Phase);
    }

    /// <summary>
    /// A publication that FAILS does NOT stop the installation: the projection is a
    /// complement and never the reason of life of the client (AD-1, AD-22).
    /// </summary>
    [Fact]
    public async Task A_publication_that_fails_does_not_abort_the_operation()
    {
        var tracker = new ActiveInstallations();

        tracker.ConfigureStatePublication(_ => throw new InvalidOperationException("el servidor no está"));

        var operationId = tracker.Begin(ClientId, GameId);

        // AD-1, AD-22: la publicación es de MEJOR ESFUERZO, así que la operación sigue.
        await tracker.PublishStateAsync(TestContext.Current.CancellationToken);

        Assert.Null(tracker.PhaseOf(operationId));
        Assert.Single(tracker.ListActiveOperations());
    }

    /// <summary>
    /// A publication that is CANCELLED keeps the cancellation: una instalación cancelada
    /// no tiene que publicar nada más (AD-1).
    /// </summary>
    [Fact]
    public async Task A_cancelled_publication_keeps_the_cancellation()
    {
        var tracker = new ActiveInstallations();

        using var cancellation = new CancellationTokenSource();

        tracker.ConfigureStatePublication(_ =>
        {
            cancellation.Cancel();

            throw new OperationCanceledException(cancellation.Token);
        });

        tracker.Begin(ClientId, GameId);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => tracker.PublishStateAsync(cancellation.Token));
    }

    /// <summary>
    /// A tracker WITHOUT a linked publication does NOT break: el cliente sin servidor
    /// sigue instalando (AD-1, AD-22).
    /// </summary>
    [Fact]
    public async Task A_tracker_without_a_linked_publication_does_not_break()
    {
        var tracker = new ActiveInstallations();

        tracker.Begin(ClientId, GameId);

        await tracker.PublishStateAsync(TestContext.Current.CancellationToken);

        Assert.Single(tracker.ListActiveOperations());
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
}
