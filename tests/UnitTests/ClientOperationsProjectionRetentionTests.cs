using LANPartyHub.Server.Application.Clients;
using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The RETENTION and the EXPIRY of the projection of the operations (AD-22, AD-33).
///
/// <para>
/// La proyección de operaciones y de resultados es memoria de estado VIVO, así que su
/// memoria está ACOTADA por el NÚMERO (el tope de resultados por cliente) y por el TIEMPO
/// (la caducidad del resultado terminal y de la operación en curso que nadie declara)
/// (AD-22, AD-33).
/// </para>
/// <para>
/// El reloj es CONTROLADO, así que la caducidad se puede ejercitar con un instante
/// conocido y sin esperar (AD-16).
/// </para>
/// </summary>
public sealed class ClientOperationsProjectionRetentionTests
{
    private const string ClientId = "0193f2a1b2c34d4e5f60718293a4b5c6";
    private const string OperationId = "0193f2a1-b2c3-4d4e-5f60-718293a4b5c9";

    /// <summary>
    /// The cap of the TERMINAL results keeps the MOST RECENT ones of the client and
    /// releases the oldest, así que la memoria que consume la proyección queda acotada por
    /// el número (AD-22).
    /// </summary>
    [Fact]
    public void The_cap_keeps_the_most_recent_results_and_releases_the_oldest()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        // Se declaran MÁS resultados que el tope, uno por segundo, así que cada uno tiene
        // su propio instante y «más reciente» NO es ambiguo (AD-22).
        for (var index = 0; index <= ClientOperationsProjection.MaxOutcomesPerClient; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));

            projection.RecordOutcome(ClientId, $"operacion-{index:00}", OperationOutcome.Succeeded);
        }

        var outcomes = projection.ListOutcomes(ClientId);

        // AD-22: el tope por cliente conserva EXACTAMENTE los resultados más recientes.
        Assert.Equal(ClientOperationsProjection.MaxOutcomesPerClient, outcomes.Count);

        // El resultado MÁS VIEJO se suelta: el que se declaró primero.
        Assert.DoesNotContain(
            outcomes,
            outcome => outcome.OperationId == "operacion-00");

        // Y los que quedan son los MÁS RECIENTES.
        Assert.Contains(outcomes, outcome => outcome.OperationId == "operacion-50");
        Assert.Contains(
            outcomes,
            outcome => outcome.OperationId == $"operacion-{ClientOperationsProjection.MaxOutcomesPerClient - 1:00}");
    }

    /// <summary>
    /// The cap does NOT touch the results of ANOTHER client, así que el tope es POR
    /// CLIENTE y no global (AD-22).
    /// </summary>
    [Fact]
    public void The_cap_does_not_release_the_results_of_another_client()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);
        const string otherClientId = "0193f2a1b2c34d4e5f60718293a4b5c7";

        for (var index = 0; index <= ClientOperationsProjection.MaxOutcomesPerClient; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));

            projection.RecordOutcome(ClientId, $"operacion-{index:00}", OperationOutcome.Succeeded);
        }

        // El otro cliente declara un resultado propio: el tope del primero NO lo suelta.
        projection.RecordOutcome(otherClientId, "operacion-propia", OperationOutcome.Succeeded);

        Assert.Single(projection.ListOutcomes(otherClientId));
    }

    /// <summary>
    /// A TERMINAL result that is OLDER than the expiry leaves the projection, así que el
    /// resultado deja de ser «reciente» cuando deja de serlo (AD-22, AD-33).
    /// </summary>
    [Fact]
    public void A_terminal_result_older_than_the_expiry_leaves_the_projection()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.RecordOutcome(ClientId, OperationId, OperationOutcome.PackageWithdrawn);

        Assert.Single(projection.ListOutcomes(ClientId));

        // AD-22: el resultado que envejece se suelta al leer.
        clock.Advance(ClientOperationsProjection.OutcomeExpiry + TimeSpan.FromSeconds(1));

        Assert.Empty(projection.ListOutcomes(ClientId));
    }

    /// <summary>
    /// An operation in course that the client does not report anymore EXPIRES, así que la
    /// proyección no conserva operaciones de un equipo que desapareció (AD-22).
    /// </summary>
    [Fact]
    public void An_operation_in_course_that_is_not_reported_anymore_expires()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.ReportTransfer(ClientId, OperationId, 512, 2048, DownloadState.Downloading);

        Assert.Single(projection.ListActiveOperations(ClientId));

        // AD-22: la operación que nadie declara deja de estar «en curso» para nadie.
        clock.Advance(ClientOperationsProjection.OperationExpiry + TimeSpan.FromSeconds(1));

        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// An operation in course that the client reports AGAIN before the expiry SURVIVES,
    /// así que la expiración solo suelta lo que nadie declara (AD-22).
    /// </summary>
    [Fact]
    public void An_operation_that_the_client_reports_again_survives_the_expiry()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.ReportTransfer(ClientId, OperationId, 512, 2048, DownloadState.Downloading);

        clock.Advance(ClientOperationsProjection.OperationExpiry - TimeSpan.FromSeconds(1));

        // El cliente vuelve a declarar su operación: la proyección la conserva.
        projection.ReportTransfer(ClientId, OperationId, 1024, 2048, DownloadState.Downloading);

        clock.Advance(TimeSpan.FromSeconds(1));

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(1024, operation.BytesTransferred);
    }

    /// <summary>
    /// A LATE progress does NOT resurrect a TERMINATED operation: ni después de que su
    /// result EXPIRED, ni después de que the cap released it, porque el recuerdo de la
    /// terminación vive MÁS que el resultado (AD-22, AD-33).
    /// </summary>
    [Fact]
    public void A_late_progress_does_not_resurrect_a_terminated_operation_after_its_result_expired()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.RecordOutcome(ClientId, OperationId, OperationOutcome.Succeeded);

        // AD-22: el resultado CADUCA, así que la vista ya no lo muestra.
        clock.Advance(ClientOperationsProjection.OutcomeExpiry + TimeSpan.FromSeconds(1));

        Assert.Empty(projection.ListOutcomes(ClientId));

        // AD-22, AD-33: el progreso TARDÍO no resucita la operación.
        projection.ReportTransfer(ClientId, OperationId, 2048, 2048, DownloadState.Downloading);

        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// A LATE progress does NOT resurrect a TERMINATED operation whose result was
    /// RELEASED by the cap, así que la deduplicación NO depende de que el resultado siga
    /// visible (AD-22, AD-33).
    /// </summary>
    [Fact]
    public void A_late_progress_does_not_resurrect_a_terminated_operation_released_by_the_cap()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);
        const string releasedOperationId = "operacion-soltada";

        // El resultado de esta operación es el MÁS VIEJO, así que el tope lo suelta.
        projection.RecordOutcome(ClientId, releasedOperationId, OperationOutcome.Succeeded);

        for (var index = 0; index <= ClientOperationsProjection.MaxOutcomesPerClient; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));

            projection.RecordOutcome(ClientId, $"operacion-{index:00}", OperationOutcome.Succeeded);
        }

        // AD-22: el resultado ya NO está en la vista.
        Assert.DoesNotContain(
            projection.ListOutcomes(ClientId),
            outcome => outcome.OperationId == releasedOperationId);

        // AD-22, AD-33: el progreso TARDÍO no resucita la operación.
        projection.ReportTransfer(ClientId, releasedOperationId, 2048, 2048, DownloadState.Downloading);

        Assert.DoesNotContain(
            projection.ListActiveOperations(ClientId),
            operation => operation.OperationId == releasedOperationId);
    }

    /// <summary>
    /// A LATE phase does NOT resurrect a TERMINATED operation either, así que la fase de
    /// una instalación que ya terminó tampoco la vuelve a abrir (AD-22, AD-33).
    /// </summary>
    [Fact]
    public void A_late_phase_does_not_resurrect_a_terminated_operation()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.ReportPhase(ClientId, OperationId, "Extracting", InstallationState.Installing);

        projection.RecordOutcome(ClientId, OperationId, OperationOutcome.Failed);

        clock.Advance(ClientOperationsProjection.OutcomeExpiry + TimeSpan.FromSeconds(1));

        // AD-22, AD-33: la fase tardía no resucita la operación.
        projection.ReportPhase(ClientId, OperationId, "ApplyingManifest", InstallationState.Installing);

        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// The memories of the terminations do NOT grow without limit: tras pasar la
    /// retención, una operación terminada ya puede volver a declararse (AD-22, AD-33).
    /// </summary>
    [Fact]
    public void A_terminated_operation_can_be_declared_again_after_the_termination_retention()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.ReportPhase(ClientId, OperationId, "Extracting", InstallationState.Installing);

        projection.RecordOutcome(ClientId, OperationId, OperationOutcome.Succeeded);

        // Mientras el recuerdo vive, la operación NO vuelve a declararse.
        projection.ReportPhase(ClientId, OperationId, "ApplyingManifest", InstallationState.Installing);

        Assert.Empty(projection.ListActiveOperations(ClientId));

        // AD-22, AD-33: pasado el recuerdo de la terminación, la operación ya puede volver
        // a declararse, así que el recuerdo no crece sin límite.
        clock.Advance(ClientOperationsProjection.TerminationRetention + TimeSpan.FromSeconds(1));

        projection.ReportPhase(ClientId, OperationId, "ApplyingManifest", InstallationState.Installing);

        var operation = Assert.Single(projection.ListActiveOperations(ClientId));

        Assert.Equal(OperationId, operation.OperationId);
        Assert.Equal("ApplyingManifest", operation.Phase);
    }

    /// <summary>
    /// The snapshot of the client does NOT resurrect a TERMINATED operation either: la
    /// instantánea puede llegar RETRASADA, después del resultado terminal (AD-22, AD-33).
    /// </summary>
    [Fact]
    public void A_late_snapshot_does_not_resurrect_a_terminated_operation()
    {
        var clock = new ManualTimeProvider();
        var projection = new ClientOperationsProjection(clock);

        projection.RecordOutcome(ClientId, OperationId, OperationOutcome.Succeeded);

        clock.Advance(ClientOperationsProjection.OutcomeExpiry + TimeSpan.FromSeconds(1));

        projection.ReplaceActiveOperations(
            ClientId,
            [
                new ClientStateChanged.ActiveOperation(
                    OperationId,
                    ClientOperationsProjection.InstallationOperationTypeName,
                    nameof(InstallationState.Installing))
            ]);

        Assert.Empty(projection.ListActiveOperations(ClientId));
    }

    /// <summary>
    /// Clock that only moves when the test moves it, así que la caducidad se puede
    /// ejercitar con un instante conocido y sin esperar (AD-16).
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _wallClock = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _wallClock;

        /// <summary>
        /// Moves the clock forward, así que el instante que mide la caducidad avanza.
        /// </summary>
        /// <param name="delta">Time that the clock advances.</param>
        public void Advance(TimeSpan delta)
        {
            _timestamp += delta.Ticks;
            _wallClock += delta;
        }
    }
}
