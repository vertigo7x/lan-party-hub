using LANPartyHub.Client.Application.GameLauncher;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The ROUTING of the intention of the PRIMARY action of a blocked installation
/// (FR-9, AC1, AC2, UX-DR-17, UX-DR-20, AD-1).
///
/// <para>
/// La primaria de la tabla de acciones es POLIMÓRFICA —«Reparar» o «Descargar de
/// nuevo»— así que la intención que se registra depende de la acción que el jugador
/// pulsó: «Reparar» repara y «Descargar de nuevo» re-descarga, y la re-descarga NUNCA
/// llega al puerto de reparación (AC1, AC2, UX-DR-20).
/// </para>
/// </summary>
public sealed class PlayabilityIntentServiceTests
{
    private const string GameId = "0193f2a1b2c34d4e5f60718293a4b5c6";

    [Fact]
    public async Task The_repair_registers_the_intention_of_repairing()
    {
        // AC1, UX-DR-20: «Reparar» reparte la intención de REPARAR.
        var port = new RecordingIntentPort();

        var service = new PlayabilityIntentService(port);

        await service.RecordPrimaryAsync(
            GameId,
            PlayabilityAction.Repair,
            TestContext.Current.CancellationToken);

        Assert.Equal([GameId], port.Repairs);
        Assert.Empty(port.Redownloads);
    }

    [Fact]
    public async Task The_redownload_registers_the_intention_of_downloading_again()
    {
        // AC2: «Descargar de nuevo» registra la intención de RE-DESCARGAR, y NO la de
        // reparar: el puerto de reparación queda intacto (AC2).
        var port = new RecordingIntentPort();

        var service = new PlayabilityIntentService(port);

        await service.RecordPrimaryAsync(
            GameId,
            PlayabilityAction.Redownload,
            TestContext.Current.CancellationToken);

        Assert.Equal([GameId], port.Redownloads);
        Assert.Empty(port.Repairs);
    }

    [Fact]
    public async Task The_redownload_never_reaches_the_port_of_the_repair()
    {
        // AC2: la ruta es EXCLUSIVA, así que el puerto de reparación no recibe NUNCA una
        // re-descarga (AC2, UX-DR-20).
        var port = new RecordingIntentPort();

        var service = new PlayabilityIntentService(port);

        await service.RecordPrimaryAsync(
            GameId,
            PlayabilityAction.Redownload,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(port.Repairs, repair => repair == GameId);
    }

    [Fact]
    public async Task The_diagnose_registers_the_intention_of_opening_the_registered_error()
    {
        // AC1, AC2: la secundaria es «Diagnosticar» y registra su propia intención.
        var port = new RecordingIntentPort();

        var service = new PlayabilityIntentService(port);

        await service.RecordDiagnoseAsync(GameId, TestContext.Current.CancellationToken);

        Assert.Equal([GameId], port.Diagnoses);
        Assert.Empty(port.Repairs);
        Assert.Empty(port.Redownloads);
    }

    [Fact]
    public async Task The_service_without_a_port_cannot_be_built()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayabilityIntentService(null!));
    }

    /// <summary>
    /// Port of the registry of the intentions that RECORDS what it was asked (AC1, AC2).
    /// </summary>
    private sealed class RecordingIntentPort : IPlayabilityIntentPort
    {
        /// <summary>The identifiers whose repair was recorded.</summary>
        public List<string> Repairs { get; } = [];

        /// <summary>The identifiers whose re-download was recorded.</summary>
        public List<string> Redownloads { get; } = [];

        /// <summary>The identifiers whose diagnosis was recorded.</summary>
        public List<string> Diagnoses { get; } = [];

        /// <inheritdoc />
        public Task RecordRepairAsync(string gameId, CancellationToken cancellationToken)
        {
            Repairs.Add(gameId);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RecordRedownloadAsync(string gameId, CancellationToken cancellationToken)
        {
            Redownloads.Add(gameId);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RecordDiagnoseAsync(string gameId, CancellationToken cancellationToken)
        {
            Diagnoses.Add(gameId);

            return Task.CompletedTask;
        }
    }
}
