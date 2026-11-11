using LANPartyHub.Client.Application.Diagnostics;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.GameLauncher;

/// <summary>
/// The use case that LAUNCHES a videogame (AD-19, FR-9, AC1, AC2, AC5).
///
/// <para>
/// El caso de uso es el ÚNICO dueño del ORDEN NORMATIVO del lanzamiento: (1) inspeccionar
/// y CERTIFICAR la instalación; (2) resolver el ejecutable y el directorio de trabajo
/// contra la raíz de instalación, con el confine de AD-9; (3) comprobar que el ejecutable
/// EXISTE; y sólo entonces (4) arrancar el proceso con sus argumentos y su directorio de
/// trabajo (AD-19, AC1).
/// </para>
/// <para>
/// Ningún proceso se arranca cuando la instalación NO está certificada como válida: la
/// certificación es la única definición de «instalación válida» del sistema, así que el
/// lanzador NO declara por su cuenta si la instalación vale (AD-1, AC5).
/// </para>
/// <para>
/// La configuración de lanzamiento es la del MANIFIESTO FIRMADO y no la del snapshot del
/// catálogo: el snapshot no lleva firma y lo que se ejecuta no puede depender de un dato
/// sin firmar (AD-2).
/// </para>
/// <para>
/// Cada fallo produce un `ApiError` del catálogo con su CAUSA y la RUTA EXACTA cuando el
/// problema es de ruta, y el cliente presenta las tres partes tal cual (AD-15, AD-25).
/// </para>
/// </summary>
public sealed class GameLaunchService
{
    private readonly IInstallationInspector _inspector;
    private readonly IInstallPathResolver _paths;
    private readonly IGameProcessRunner _runner;
    private readonly RecordClientError? _errorRecorder;
    private readonly ILogger<GameLaunchService> _logger;

    /// <summary>Creates the use case of the launch of a videogame.</summary>
    /// <param name="inspector">Port of the inspection and certification of the installation.</param>
    /// <param name="paths">Resolver of the paths of the installation, which confines them (AD-9).</param>
    /// <param name="runner">Port of the run of the videogame process (AD-19).</param>
    /// <param name="logger">Log of the use case.</param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors del lanzamiento, or <c>null</c> when the client
    /// does not register them. Sin registrador el lanzamiento SIGUE: el diagnóstico es un
    /// complemento y no su motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    public GameLaunchService(
        IInstallationInspector inspector,
        IInstallPathResolver paths,
        IGameProcessRunner runner,
        ILogger<GameLaunchService> logger,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);

        _inspector = inspector;
        _paths = paths;
        _runner = runner;
        _logger = logger;
        _errorRecorder = errorRecorder;
    }

    /// <summary>
    /// Launches the videogame of a game, after certifying its installation (AD-19, AC1).
    ///
    /// <para>
    /// El lanzamiento NO arranca ningún proceso cuando la instalación no está certificada
    /// o cuando el ejecutable falta o sale de la raíz, y el error NOMBRA la ruta exacta
    /// (AC1, AD-9).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame to launch.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the launch.</returns>
    public async Task<GameLaunchOutcome> LaunchAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        // AD-23: el lanzamiento es una UNIDAD DE TRABAJO, así que lleva su propio
        // identificador opaco y UUID v7, el mismo que viaja en el log del cliente y en el
        // del servidor, así que un error del servidor y su correspondiente en el cliente
        // se pueden UNIR (AC3, AR-20).
        var operationId = Guid.CreateVersion7().ToString("N");

        // PASO 1: la instalación se INSPECTA y se CERTIFICA (AD-19, AC1, AC5).
        var inspection = await _inspector
            .InspectAsync(gameId, cancellationToken)
            .ConfigureAwait(false);

        if (inspection is null)
        {
            return await RejectAsync(
                ErrorCatalog.GameLaunchInstallationInvalidCode,
                $"El cliente no tiene ninguna instalación registrada para el juego '{gameId}', así que no se ejecuta ningún proceso.",
                operationId,
                cancellationToken).ConfigureAwait(false);
        }

        if (!inspection.Verdict.IsInstalled)
        {
            var reason = inspection.Verdict.ReasonText
                ?? $"La instalación del juego '{gameId}' no está certificada como válida.";

            return await RejectAsync(
                ErrorCatalog.GameLaunchInstallationInvalidCode,
                $"{reason} No se ejecuta ningún proceso.",
                operationId,
                cancellationToken).ConfigureAwait(false);
        }

        // PASO 2: el ejecutable se RESUELVE contra la raíz de instalación y no sale de ella
        // (AD-9).
        string executablePath;

        try
        {
            executablePath = _paths.ResolveInsideRoot(inspection.LaunchSettings.ExecutablePath);
        }
        catch (InstallPathOutsideRootException exception)
        {
            return await RejectAsync(
                ErrorCatalog.GameLaunchPathOutsideRootCode,
                $"El ejecutable '{exception.RelativePath}' del juego '{gameId}' resuelve en '{exception.CanonicalPath}', que está fuera de la raíz de instalación '{exception.RootPath}'. No se ejecuta ningún proceso.",
                operationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return await RejectAsync(
                ErrorCatalog.GameLaunchExecutableMissingCode,
                $"El manifiesto firmado del juego '{gameId}' no declara una ruta de ejecutable válida, así que no se ejecuta ningún proceso.",
                operationId,
                cancellationToken).ConfigureAwait(false);
        }

        // PASO 3: el ejecutable TIENE QUE EXISTIR, y el error nombra su ruta exacta (AC1).
        if (!File.Exists(executablePath))
        {
            return await RejectAsync(
                ErrorCatalog.GameLaunchExecutableMissingCode,
                $"El ejecutable '{executablePath}' del juego '{gameId}' no existe. No se ejecuta ningún proceso.",
                operationId,
                cancellationToken).ConfigureAwait(false);
        }

        // El directorio de trabajo también se RESUELVE contra la raíz, porque es una ruta
        // del manifiesto y el confine de AD-9 no tiene excepción (AD-9).
        if (!TryResolveWorkingDirectory(inspection, gameId, out var workingDirectory, out var rejection))
        {
            return await RejectAsync(
                rejection!.Error!.Code,
                rejection.Reason!,
                operationId,
                cancellationToken).ConfigureAwait(false);
        }

        var arguments = inspection.LaunchSettings.Arguments ?? [];

        // PASO 4: el juego arranca como PROCESO PROPIO, con sus argumentos y su
        // directorio de trabajo (AD-19, AC1, AC3).
        try
        {
            var handle = await _runner
                .StartAsync(executablePath, arguments, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "El juego {GameId} arrancó con el ejecutable '{ExecutablePath}', {ArgumentCount} argumentos y el directorio de trabajo '{WorkingDirectory}'.",
                gameId,
                executablePath,
                arguments.Count,
                workingDirectory);

            return GameLaunchOutcome.Launched(handle, executablePath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "El juego {GameId} no arrancó: el proceso falló al iniciarse.",
                gameId);

            return await RejectAsync(
                ErrorCatalog.GameLaunchProcessFailedCode,
                $"El proceso del juego '{gameId}' no arrancó a partir del ejecutable '{executablePath}'. No se ha quedado ningún proceso en marcha.",
                operationId,
                cancellationToken).ConfigureAwait(false);
        }
    }


    /// <summary>
    /// Resolves the working directory of the launch against the installation root (AD-9).
    ///
    /// <para>
    /// Un directorio de trabajo que sale de la raíz se RECHAZA con su ruta canónica, y el
    /// rechazo IMPIDE el lanzamiento: el confine de AD-9 no tiene excepción para el
    /// directorio de trabajo (AD-9, AC1).
    /// </para>
    /// </summary>
    /// <param name="inspection">Inspection of the installation.</param>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="rejection">The rejection of the launch, or <c>null</c>.</param>
    /// <returns>The canonical absolute path of the working directory.</returns>
    private bool TryResolveWorkingDirectory(
        InstallationInspection inspection,
        string gameId,
        out string workingDirectory,
        out GameLaunchOutcome? rejection)
    {
        rejection = null;

        var declared = inspection.LaunchSettings.WorkingDirectory;

        // Un directorio de trabajo VACÍO significa la raíz de la instalación: el manifiesto
        // puede no declarar uno, y el proceso arranca en la raíz que ya está confinada.
        if (string.IsNullOrWhiteSpace(declared))
        {
            workingDirectory = inspection.RootPath;

            return true;
        }

        try
        {
            workingDirectory = _paths.ResolveInsideRoot(declared);

            return true;
        }
        catch (InstallPathOutsideRootException exception)
        {
            rejection = GameLaunchOutcome.Rejected(
                ErrorCatalog.Resolve(ErrorCatalog.GameLaunchPathOutsideRootCode),
                $"El directorio de trabajo '{exception.RelativePath}' del juego '{gameId}' resuelve en '{exception.CanonicalPath}', que está fuera de la raíz de instalación '{exception.RootPath}'. No se ejecuta ningún proceso.");

            workingDirectory = string.Empty;

            return false;
        }
    }

    /// <summary>
    /// Builds the rejection of a launch, with its cause and the exact path when the
    /// problem is a path (AC1, AD-15, AD-25).
    ///
    /// <para>
    /// El rechazo queda REGISTRADO en el registro de diagnóstico del cliente, con su
    /// código y sus tres partes, y ASOCIADO a la operación de lanzamiento que lo produjo
    /// (AD-11, AD-15, AC2, NFR-7).
    /// </para>
    /// </summary>
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <param name="operationId">
    /// Identifier of the launch operation, so the error can be JOINED with the log of the
    /// server (AD-23).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task with the outcome of the rejection.</returns>
    private async Task<GameLaunchOutcome> RejectAsync(
        string code,
        string reason,
        string operationId,
        CancellationToken cancellationToken)
    {
        var error = ErrorCatalog.Resolve(code);

        _logger.LogWarning(
            "El lanzamiento se rechaza con el código {Code}: {Reason}",
            code,
            reason);

        // AD-1, AD-11: el registro del diagnóstico NUNCA impide que el jugador siga: el
        // error queda persistido en el cliente y un fallo del registrador se anota y se
        // continúa (NFR-7).
        await RecordErrorAsync(error, operationId, cancellationToken).ConfigureAwait(false);

        return GameLaunchOutcome.Rejected(error, reason);
    }

    /// <summary>
    /// REGISTERS an error del lanzamiento en el registro de diagnóstico del cliente
    /// (AD-11, AD-15, NFR-7, AC2).
    ///
    /// <para>
    /// El registro NUNCA impide que el jugador siga: el diagnóstico es un complemento y
    /// no su motivo de vida, así que un fallo del registrador se anota y se continúa
    /// (AD-1).
    /// </para>
    /// </summary>
    /// <param name="error">Error of the catalog that explains the failure.</param>
    /// <param name="operationId">Identifier of the launch operation (AD-23).</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the error has been registered, or has failed.</returns>
    private async Task RecordErrorAsync(
        ApiError error,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (_errorRecorder is null)
        {
            return;
        }

        try
        {
            await _errorRecorder
                .RecordAsync(error, operationId, OperationTypeNames.Launch, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "No se pudo registrar el error {Code} de la operación {OperationId} en el registro de diagnóstico del cliente.",
                error.Code,
                operationId);
        }
    }
}
