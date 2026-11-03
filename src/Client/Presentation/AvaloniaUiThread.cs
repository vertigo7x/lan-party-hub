using Avalonia.Threading;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Presentation;

/// <summary>
/// Implementation of the UI thread of the client with the dispatcher of Avalonia (AC3, AC4,
/// AC5).
///
/// <para>
/// La notificación se ENCOLA en el hilo de la interfaz porque un control de Avalonia SOLO
/// se toca desde el hilo que lo posee. El supervisor de la disponibilidad y la composición
/// de la sincronización publican desde el grupo de hilos, así que sin este encolado el
/// <c>SetValue</c> del punto de presencia lanza <c>InvalidOperationException</c> y el panel
/// no se repinta ni reconecta nunca (AC3, AC4, AC5).
/// </para>
/// <para>
/// El puerto está declarado en la capa de aplicación y esta implementación vive en la de
/// presentación: el que publica depende del port y nunca del despachador (AD-1).
/// </para>
/// </summary>
public sealed class AvaloniaUiThread : IUiThread
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Si el llamador YA está en el hilo de la interfaz, el trabajo se ejecuta de
        // INMEDIATO y no se encola: encolar sin necesidad retrasaría el repintado y
        // rompería el orden de las notificaciones (AC3).
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();

            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
