using System.Diagnostics;
using LANPartyHub.Client.Application.Ports;
using Microsoft.Extensions.Logging;

// El TIPO del proceso se importa con un alias porque el espacio de nombres de este
// archivo se llama «Process» y el nombre corto chocaría con él.
using GameProcess = System.Diagnostics.Process;

namespace LANPartyHub.Client.Infrastructure.Process;

/// <summary>
/// Adapter of the run of a videogame process (AD-19, FR-9, AC1, AC3).
///
/// <para>
/// El adaptador arranca el juego como un PROCESO PROPIO: <c>UseShellExecute = false</c>
/// y <c>CreateNoWindow = true</c>, así que el juego NO es una ventana hija del cliente, no
/// abre una consola y el cliente queda en segundo plano (AC3, UX-DR-43).
/// </para>
/// <para>
/// El cliente NO captura teclas: no registra ningún gancho global de teclado ni de
/// ventana, y el proceso del juego no hereda ningún gancho del cliente (AC4, UX-DR-43).
/// </para>
/// <para>
/// Los argumentos se entregan EN ORDEN, uno por argumento, sin la línea de comandos
/// concatenada: el ejecutable decide cómo los interpreta (AD-19, AC1).
/// </para>
/// </summary>
public sealed class SystemGameProcessRunner : IGameProcessRunner
{
    private readonly ILogger<SystemGameProcessRunner> _logger;

    /// <summary>Creates the adapter of the run of a videogame process.</summary>
    /// <param name="logger">Log of the adapter.</param>
    public SystemGameProcessRunner(ILogger<SystemGameProcessRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IGameProcessHandle> StartAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,

            // El juego se ejecuta FUERA de la ventana del cliente: sin intérprete de
            // comandos, sin ventana propia y sin que el cliente espere al proceso (AC3,
            // UX-DR-43).
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,

            // El juego hereda la salida estándar del cliente para que su diagnóstico
            // llegue al log y no se pierda (NFR-7).
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = GameProcess.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"El sistema no devolvió ningún proceso al arrancar '{executablePath}'.");

        _logger.LogInformation(
            "El proceso del juego arrancó con identificador {ProcessId} a partir de '{ExecutablePath}'.",
            process.Id,
            executablePath);

        return Task.FromResult<IGameProcessHandle>(new SystemGameProcessHandle(process));
    }

    /// <summary>
    /// Handle of a videogame process that the client started (AD-19, AC2).
    /// </summary>
    private sealed class SystemGameProcessHandle : IGameProcessHandle
    {
        private readonly GameProcess _process;

        public SystemGameProcessHandle(GameProcess process) => _process = process;

        /// <inheritdoc />
        public int ProcessId => _process.Id;

        /// <inheritdoc />
        public bool IsRunning
        {
            get
            {
                try
                {
                    return !_process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    // Un proceso que ya se liberó NO está en ejecución.
                    return false;
                }
            }
        }
    }
}
