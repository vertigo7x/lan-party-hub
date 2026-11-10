using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The projection of the OPERATIONS that the clients are running right now
/// (AD-22, FR-10).
///
/// <para>
/// La lista que el cliente publica es la lista COMPLETA de sus operaciones en curso, así
/// que al reemplazar una operación NO se pierde el PROGRESO REAL que el cliente ya
/// declaró en otro evento: la fase de una instalación y los bytes de una descarga son
/// datos REALES, y la lista de operaciones en curso declara el TIPO y el ESTADO, no el
/// progreso (AD-22, FR-10).
/// </para>
/// </summary>
public sealed class ClientOperationsProjectionTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";

    [Fact]
    public void A_resumed_download_uses_the_latest_real_progress_state()
    {
        var projection = new ClientOperationsProjection();
        projection.ReportTransfer(ClientId, OperationId, 10, 100, DownloadState.Paused);
        projection.ReportTransfer(ClientId, OperationId, 20, 100, DownloadState.Downloading);
        projection.ReplaceActiveOperations(ClientId,
            [new ClientStateChanged.ActiveOperation(OperationId, "download", nameof(DownloadState.Paused))]);

        Assert.Equal(StateDisplayMap.GetText(DownloadState.Downloading),
            Assert.Single(projection.ListActiveOperations(ClientId)).StateText);
    }

    [Fact]
    public void Progress_after_privileged_confirmation_shows_installing_again()
    {
        var projection = new ClientOperationsProjection();
        projection.ReportPhase(ClientId, OperationId, "AwaitingConfirmation", InstallationState.AwaitingConfirmation);
        projection.ReportPhase(ClientId, OperationId, "ApplyingManifest", InstallationState.Installing);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));
        Assert.Equal("ApplyingManifest", operation.Phase);
        Assert.Equal(StateDisplayMap.GetText(InstallationState.Installing), operation.StateText);
    }

    [Fact]
    public void Terminal_download_progress_rejects_late_nonterminal_progress()
    {
        var projection = new ClientOperationsProjection();
        projection.ReportTransfer(ClientId, OperationId, 100, 100, DownloadState.Completed);
        projection.ReportTransfer(ClientId, OperationId, 80, 100, DownloadState.Downloading);

        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    [Fact]
    public void The_real_phase_survives_a_snapshot_that_replaces_the_operation()
    {
        var projection = new ClientOperationsProjection();

        projection.ReportPhase(ClientId, OperationId, "Extracting");

        // AD-22: la operación se vuelve a declarar en la lista de operaciones en curso,
        // así que la fase REAL que ya se declaró NO se pierde.
        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "installation",
                    nameof(InstallationState.Installing))
            ]);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(OperationId, operation.OperationId);
        Assert.Equal("installation", operation.OperationType);
        Assert.Equal("Extracting", operation.Phase);
    }

    [Fact]
    public void The_real_bytes_survive_a_snapshot_that_replaces_the_operation()
    {
        var projection = new ClientOperationsProjection();

        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "download",
                    nameof(DownloadState.Downloading))
            ]);

        projection.ReportTransfer(ClientId, OperationId, 512, 2048);

        // AD-22: la operación se vuelve a declarar y los bytes REALES que el cliente ya
        // declaró NO se pierden.
        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "download",
                    nameof(DownloadState.Downloading))
            ]);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(512, operation.BytesTransferred);
        Assert.Equal(2048, operation.TotalBytes);
    }

    [Fact]
    public void The_state_that_the_client_declares_replaces_the_previous_one()
    {
        var projection = new ClientOperationsProjection();

        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "installation",
                    nameof(InstallationState.Installing))
            ]);

        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "installation",
                    nameof(InstallationState.Installed))
            ]);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        // AD-10, AD-25: el texto del estado sale de la tabla única de microcopia y
        // REFLEJA lo que el cliente declaró en la última publicación.
        Assert.Equal(StateDisplayMap.GetText(InstallationState.Installed), operation.StateText);
    }

    [Fact]
    public void The_operation_that_the_client_no_longer_declares_leaves_the_projection()
    {
        var projection = new ClientOperationsProjection();

        projection.ReportPhase(ClientId, OperationId, "Extracting");

        projection.ReplaceActiveOperations(ClientId, []);

        // AD-22: la lista que declara el cliente es la lista COMPLETA, así que la
        // operación que ya no aparece deja de estar en curso.
        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    [Fact]
    public void An_unknown_download_state_is_rejected_without_throwing()
    {
        var projection = new ClientOperationsProjection();
        const string otherOperationId = "other-valid-operation";

        projection.ReportTransfer(ClientId, OperationId, 512, 2048, DownloadState.Downloading);
        projection.ReportTransfer(ClientId, otherOperationId, 1024, 4096, DownloadState.Downloading);

        // AD-10, AD-22: un DownloadState numérico desconocido se rechaza SOLO en esa
        // publicación. No se lanza una excepción al hub y el resto de la proyección se
        // conserva.
        projection.ReportTransfer(ClientId, "unknown-state", 4096, 4096, (DownloadState)999);

        Assert.Equal(2, projection.ListActiveOperations(ClientId).Count);
        Assert.DoesNotContain(
            projection.ListActiveOperations(ClientId),
            operation => operation.OperationId == "unknown-state");
    }

    [Fact]
    public void A_late_phase_does_not_resurrect_a_terminated_operation()
    {
        var projection = new ClientOperationsProjection();

        projection.ReportPhase(ClientId, OperationId, "Extracting");

        Assert.True(projection.RecordOutcome(
            ClientId,
            OperationId,
            OperationOutcome.PackageWithdrawn));

        // AD-22, AD-33: el progreso tardío de una operación que ya terminó no la
        // resucita.
        projection.ReportPhase(ClientId, OperationId, "Installing");

        Assert.Empty(projection.ListActiveOperations(ClientId));
        Assert.Single(projection.ListOutcomes(ClientId));
    }

    [Fact]
    public void An_unknown_state_does_not_corrupt_the_projection_of_the_other_operations()
    {
        var projection = new ClientOperationsProjection();
        const string otherOperationId = "other-valid-operation";

        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "installation",
                    nameof(InstallationState.Installing)),
                new ClientStateChanged.ActiveOperation(
                    otherOperationId,
                    "installation",
                    nameof(InstallationState.Installing))
            ]);

        // AD-10, AD-25: un estado que la tabla NO reconoce no se inventa: la operación se
        // descarta y el resto de la proyección se conserva (AD-22).
        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    "installation",
                    "UnEstadoQueNoExiste"),
                new ClientStateChanged.ActiveOperation(
                    otherOperationId,
                    "installation",
                    nameof(InstallationState.Installing))
            ]);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(otherOperationId, operation.OperationId);
        Assert.Equal(StateDisplayMap.GetText(InstallationState.Installing), operation.StateText);
    }
}
