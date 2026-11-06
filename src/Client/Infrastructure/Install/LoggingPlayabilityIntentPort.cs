using LANPartyHub.Client.Application.GameLauncher;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Install;

/// <summary>
/// Adapter of the REGISTRY of the intentions that the player expresses with a blocked
/// installation (FR-9, AC1, AC2, AD-1, AD-15).
///
/// <para>
/// La reparación, la re-descarga y el registro de diagnóstico son de otras historias
/// (8.3, 5.x/6.x y Epic 11), así que este adaptador REGISTRA la intención y nada más:
/// el seam queda habilitado y la ejecución vive donde corresponde (AC1, AC2).
/// </para>
/// <para>
/// El registro va al LOG del cliente con su CÓDIGO estable, así que la métrica por
/// código puede contarlo sin que el código sea nunca el texto principal (AD-15,
/// AD-25).
/// </para>
/// </summary>
public sealed class LoggingPlayabilityIntentPort : IPlayabilityIntentPort
{
    private readonly ILogger<LoggingPlayabilityIntentPort> _logger;

    /// <summary>
    /// Creates the adapter of the registry of the intentions.
    /// </summary>
    /// <param name="logger">Log of the client.</param>
    public LoggingPlayabilityIntentPort(ILogger<LoggingPlayabilityIntentPort> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public Task RecordRepairAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        _logger.LogInformation(
            "El jugador pidió reparar la instalación del juego {GameId}: la reparación la ejecuta la historia 8.3 y aquí sólo queda registrada la intención.",
            gameId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordRedownloadAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        _logger.LogInformation(
            "El jugador pidió descargar de nuevo el juego {GameId}: la re-descarga la ejecutan las historias 5.x/6.x y aquí sólo queda registrada la intención.",
            gameId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordDiagnoseAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        _logger.LogInformation(
            "El jugador pidió diagnosticar el juego {GameId}: el registro de diagnóstico es de Epic 11 y aquí sólo queda registrada la intención.",
            gameId);

        return Task.CompletedTask;
    }
}
