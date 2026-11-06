using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Presentation.Status;

/// <summary>
/// View model of the state of the game that is RUNNING (AD-19, AC2, AC3, UX-DR-6,
/// UX-DR-43).
///
/// <para>
/// La microcopia del estado de ejecución es DEL CLIENTE y no de <c>Shared</c>: el estado
/// de ejecución de un juego es un estado DEL CLIENTE, y el contrato compartido no crece
/// (AD-18, AD-22, AR-3).
/// </para>
/// <para>
/// El panel muestra «En ejecución» mientras el proceso vive y «Ningún juego en
/// ejecución» cuando no hay ninguno: el panel NUNCA deja un espacio vacío (AC2,
/// UX-DR-6).
/// </para>
/// <para>
/// El estado de ejecución vive EN MEMORIA del cliente: no se persiste, no hay tabla
/// nueva ni migración, y el cliente NO es dueño del ciclo de vida del juego (AD-22,
/// AC2).
/// </para>
/// </summary>
public sealed class GameRunStatusViewModel
{
    /// <summary>
    /// Text of the panel when a game is RUNNING (AC2, UX-DR-6).
    /// </summary>
    public const string RunningText = "En ejecución";

    /// <summary>
    /// Text of the panel when NO game is running (AC2, UX-DR-6).
    /// </summary>
    public const string NoGameRunningText = "Ningún juego en ejecución";

    /// <summary>
    /// Text of the label of the row of the state of execution (AC2, UX-DR-11).
    /// </summary>
    public const string GameRunLabelText = "Juego";

    private IGameProcessHandle? _handle;
    private string? _gameName;

    /// <summary>Raised when the state of execution changes and the panel has to be repainted.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether a game is RUNNING right now (AC2).
    ///
    /// <para>
    /// La pregunta la hace el proceso, no el cliente: un proceso que terminó deja de
    /// estar en ejecución aunque el handle siga guardado en memoria (AC2).
    /// </para>
    /// </summary>
    public bool IsRunning => _handle is not null && _handle.IsRunning;

    /// <summary>
    /// Text of the state of execution: «En ejecución» o «Ningún juego en ejecución»
    /// (AC2, UX-DR-6).
    /// </summary>
    public string StatusText => IsRunning ? RunningText : NoGameRunningText;

    /// <summary>
    /// Name of the game that is running, or <c>null</c> when no game is running (AC2).
    /// </summary>
    public string? GameName => _gameName;

    /// <summary>
    /// Records the game that was LAUNCHED, so the panel shows «En ejecución» (AC2).
    ///
    /// <param name="handle">Handle of the process that is running.</param>
    /// <param name="gameName">Name of the videogame that was launched.</param>
    public void ApplyLaunch(IGameProcessHandle handle, string? gameName)
    {
        ArgumentNullException.ThrowIfNull(handle);

        _handle = handle;
        _gameName = gameName;

        Changed?.Invoke();
    }

    /// <summary>
    /// Records that NO game is running, so the panel shows «Ningún juego en ejecución»
    /// (AC2).
    ///
    /// <para>
    /// La llamada es IDEMPOTENTE: el panel puede preguntar por el estado sin que nada
    /// haya cambiado, y preguntar no cambia el estado (AC2).
    /// </para>
    /// </summary>
    public void ApplyStopped()
    {
        _handle = null;
        _gameName = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Refreshes the state of execution, so a process that finished on its own stops
    /// showing «En ejecución» (AC2, UX-DR-6).
    ///
    /// <para>
    /// El refresco NO inventa un estado: si el proceso ya no vive, el handle se suelta y
    /// el panel vuelve a «Ningún juego en ejecución» (AC2).
    /// </para>
    /// </summary>
    public void Refresh()
    {
        if (_handle is not null && !_handle.IsRunning)
        {
            _handle = null;
            _gameName = null;
        }

        Changed?.Invoke();
    }
}
