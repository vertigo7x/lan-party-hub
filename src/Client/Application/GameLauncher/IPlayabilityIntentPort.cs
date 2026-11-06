namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// Port of the INTENTION that the player expresses with a blocked installation
/// (FR-9, AC1, AC2, UX-DR-17, UX-DR-20, AD-1).
///
/// <para>
/// Las acciones «Reparar», «Descargar de nuevo» y «Diagnosticar» son SEAMS habilitadas:
/// su ejecución vive en 8.3, en 5.x/6.x y en el registro de diagnóstico, y aquí el
/// puerto REGISTRA la intención (AC1, AC2).
/// </para>
/// <para>
/// El puerto existe para que la interfaz NO conozca la reparación, la re-descarga ni el
/// registro de diagnóstico: el caso de uso consume el port y la composición lo entrega
/// con su única implementación (AD-1).
/// </para>
/// </remarks>
public interface IPlayabilityIntentPort
{
    /// <summary>
    /// Records the intention of REPAIRING an installation, that is backed by the package
    /// that was already validated (AC1, UX-DR-20).
    ///
    /// <para>
    /// La intención NO repara nada por sí sola: la reparación vive en 8.3, y aquí el
    /// puerto sólo deja constancia de lo que el jugador pidió (AC1).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention has been recorded.</returns>
    Task RecordRepairAsync(string gameId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the intention of DOWNLOADING the package AGAIN, because the validated one
    /// is not available (AC2).
    ///
    /// <para>
    /// La intención NO descarga nada por sí sola: la re-descarga vive en 5.x/6.x, y aquí
    /// el puerto sólo deja constancia de lo que el jugador pidió (AC2).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention has been recorded.</returns>
    Task RecordRedownloadAsync(string gameId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the intention of OPENING the registered error of the blocked installation
    /// (AC1, AC2).
    ///
    /// <para>
    /// La intención NO abre el registro por sí sola: el registro de diagnóstico es de
    /// Epic 11, y aquí el puerto sólo deja constancia de lo que el jugador pidió (AC1).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the intention has been recorded.</returns>
    Task RecordDiagnoseAsync(string gameId, CancellationToken cancellationToken);
}
