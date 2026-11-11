namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// The case of use that REGISTERS the intention of the PRIMARY action of a blocked
/// installation (FR-9, AC1, AC2, UX-DR-17, UX-DR-20, AD-1).
///
/// <para>
/// La primaria de la tabla de acciones es POLIMÓRFICA: es «Reparar» o «Descargar de
/// nuevo» según la decisión de la instalación, así que la intención que se registra
/// depende de la acción que el jugador pulsó y NO de la etiqueta del botón (AC1, AC2).
/// </para>
/// <para>
/// El caso de uso NO repara ni descarga nada: el puerto REGISTRA la intención y la
/// ejecución vive en 8.3, en 5.x/6.x y en el registro de diagnóstico (AC1, AC2, AD-1).
/// </para>
/// <para>
/// La ruta vive AQUÍ, en la capa de aplicación, y NO en la composición: la composición
/// sólo cablea y la interfaz sólo pinta, así que la regla de qué intención corresponde a
/// qué acción tiene UN solo dueño (AD-1).
/// </para>
/// </remarks>
public sealed class PlayabilityIntentService
{
    private readonly IPlayabilityIntentPort _intents;

    /// <summary>
    /// Creates the case of use that registers the intentions of a blocked installation.
    /// </summary>
    /// <param name="intents">Port of the registry of the intentions.</param>
    public PlayabilityIntentService(IPlayabilityIntentPort intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        _intents = intents;
    }

    /// <summary>
    /// Registers the intention of the PRIMARY action that the player pressed (AC1, AC2).
    ///
    /// <para>
    /// «Reparar» registra la intención de REPARAR y «Descargar de nuevo» registra la
    /// intención de RE-DESCARGAR: la re-descarga NUNCA llega al puerto de reparación
    /// (AC1, AC2, UX-DR-20).
    /// </para>
    /// <para>
    /// «Actualizar» registra la intención de RE-DESCARGAR la versión nueva que publica el
    /// catálogo: la actualización descarga el paquete COMPLETO de la versión publicada, así
    /// que su ejecución vive en 10.2 y aquí el puerto sólo deja constancia de lo que el
    /// jugador pidió (AC1, AC4, FR-11).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="primary">The PRIMARY action that the player pressed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention has been recorded.</returns>
    public async Task RecordPrimaryAsync(
        string gameId,
        PlayabilityAction primary,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        if (primary is PlayabilityAction.Redownload or PlayabilityAction.Update)
        {
            await _intents
                .RecordRedownloadAsync(gameId, cancellationToken)
                .ConfigureAwait(true);

            return;
        }

        await _intents
            .RecordRepairAsync(gameId, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Registers the intention of OPENING the registered error of the blocked
    /// installation (AC1, AC2).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention has been recorded.</returns>
    public async Task RecordDiagnoseAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        await _intents
            .RecordDiagnoseAsync(gameId, cancellationToken)
            .ConfigureAwait(true);
    }
}
