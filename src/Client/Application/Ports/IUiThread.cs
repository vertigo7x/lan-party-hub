namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the UI THREAD of the client, so that a notification that arrives from the pool
/// of threads reaches the interface safely (AC3, AC4, AC5).
///
/// <para>
/// El supervisor de la disponibilidad publica desde el bucle de la supervisión, que va con
/// <c>ConfigureAwait(false)</c>, y la reconexión resincroniza el catálogo también fuera del
/// hilo de la interfaz. Un suscriptor que muta un control de la interfaz desde ese hilo hace
/// que la propiedad del control lance <c>InvalidOperationException</c> («The calling thread
/// cannot access this object because a different thread owns it»), la excepción sale de la
/// publicación sin capturar y el panel NO se repinta ni reconecta nunca (AC3, AC4, AC5).
/// </para>
/// <para>
/// El port está declarado en la capa de aplicación porque el supervisor y la composición
/// dependen de él y NUNCA del despachador de la interfaz (AD-1). La implementación que
/// encola en el hilo de la interfaz vive en la capa de presentación.
/// </para>
/// </summary>
public interface IUiThread
{
    /// <summary>
    /// Runs the given work in the thread of the interface (AC3, AC4, AC5).
    ///
    /// <para>
    /// La notificación NUNCA se aplica en el hilo que publica: se encola y se aplica en el
    /// hilo que posee los controles (AC3).
    /// </para>
    /// </summary>
    /// <param name="action">Work that touches the interface.</param>
    void Post(Action action);
}
