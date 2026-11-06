namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the RUN of the videogame (AD-19, FR-9, AC1, AC5).
///
/// <para>
/// El port existe para que el módulo <c>GameLauncher</c> sea AISLADO de la plataforma:
/// el caso de uso decide CUÁNDO se lanza y QUÉ se lanza, y el adaptador decide CÓMO se
/// arranca el proceso (AD-1, AD-19).
/// </para>
/// <para>
/// El juego arranca como un PROCESO PROPIO: no es una ventana hija del cliente, no
/// activa la ventana del cliente y el cliente queda en segundo plano (AC3, UX-DR-43).
/// </para>
/// <para>
/// La CONEXIÓN DIRECTA a servidores de juego y los LAUNCHERS EXTERNOS quedan fuera del
/// MVP: este port NO los expone, así que la extensión futura no se implementa antes de
/// tiempo (AD-19, AC5).
/// </para>
/// </summary>
public interface IGameProcessRunner
{
    /// <summary>
    /// Starts the videogame as its OWN process, with its arguments and its working
    /// directory (AD-19, AC1).
    ///
    /// <para>
    /// El proceso NO es una ventana hija del cliente: se lanza con su propio ejecutable,
    /// con su directorio de trabajo y con sus argumentos, y el cliente no lo espera ni
    /// lo supervisa (AC3, UX-DR-43).
    /// </para>
    /// </summary>
    /// <param name="executablePath">Absolute path of the executable to start.</param>
    /// <param name="arguments">Arguments that the executable receives, in order.</param>
    /// <param name="workingDirectory">Absolute path of the working directory of the process.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The handle of the process that was started.</returns>
    Task<IGameProcessHandle> StartAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

/// <summary>
/// The handle of a videogame process that the client started (AD-19, AC2).
///
/// <para>
/// El handle es la ÚNICA forma que el cliente tiene de preguntar si el juego sigue en
/// ejecución, y no se persiste: el estado de ejecución vive EN MEMORIA del cliente
/// (AD-22, AC2).
/// </para>
/// </summary>
public interface IGameProcessHandle
{
    /// <summary>Identifier of the process, so the client can name it in the log.</summary>
    int ProcessId { get; }

    /// <summary>
    /// Tells whether the process is STILL RUNNING (AD-19, AC2).
    ///
    /// <para>
    /// Un proceso que ya terminó NO está en ejecución, y la pregunta es la que el panel
    /// traduce en «En ejecución» o «Ningún juego en ejecución» (AC2, UX-DR-6).
    /// </para>
    /// </summary>
    /// <returns><c>true</c> when the process is still alive.</returns>
    bool IsRunning { get; }
}
