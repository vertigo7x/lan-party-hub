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
    private readonly ILogger<GameLaunchService> _logger;

    /// <summary>Creates the use case of the launch of a videogame.</summary>
    /// <param name="inspector">Port of the inspection and certification of the installation.</param>
    /// <param name="paths">Resolver of the paths of the installation, which confines them (AD-9).</param>
    /// <param name="runner">Port of the run of the videogame process (AD-19).</param>
    /// <param name="logger">Log of the use case.</param>
    public GameLaunchService(
        IInstallationInspector inspector,
        IInstallPathResolver paths,
        IGameProcessRunner runner,
        ILogger<GameLaunchService> logger)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);

        _inspector = inspector;
        _paths = paths;
        _runner = runner;
        _logger = logger;
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

        // PASO 1: la instalación se INSPECTA y se CERTIFICA (AD-19, AC1, AC5).
        var inspection = await _inspector
            .InspectAsync(gameId, cancellationToken)
            .ConfigureAwait(false);

        if (inspection is null)
        {
            return Reject(
                ErrorCatalog.GameLaunchInstallationInvalidCode,
                $"El cliente no tiene ninguna instalación registrada para el juego '{gameId}', así que no se ejecuta ningún proceso.");
        }

        if (!inspection.Verdict.IsInstalled)
        {
            var reason = inspection.Verdict.ReasonText
                ?? $"La instalación del juego '{gameId}' no está certificada como válida.";

            return Reject(
                ErrorCatalog.GameLaunchInstallationInvalidCode,
                $"{reason} No se ejecuta ningún proceso.");
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
            return Reject(
                ErrorCatalog.GameLaunchPathOutsideRootCode,
                $"El ejecutable '{exception.RelativePath}' del juego '{gameId}' resuelve en '{exception.CanonicalPath}', que está fuera de la raíz de instalación '{exception.RootPath}'. No se ejecuta ningún proceso.");
        }
        catch (ArgumentException)
        {
            return Reject(
                ErrorCatalog.GameLaunchExecutableMissingCode,
                $"El manifiesto firmado del juego '{gameId}' no declara una ruta de ejecutable válida, así que no se ejecuta ningún proceso.");
        }

        // PASO 3: el ejecutable TIENE QUE EXISTIR, y el error nombra su ruta exacta (AC1).
        if (!File.Exists(executablePath))
        {
            return Reject(
                ErrorCatalog.GameLaunchExecutableMissingCode,
                $"El ejecutable '{executablePath}' del juego '{gameId}' no existe. No se ejecuta ningún proceso.");
        }

        // El directorio de trabajo también se RESUELVE contra la raíz, porque es una ruta
        // del manifiesto y el confine de AD-9 no tiene excepción (AD-9).
        if (!TryResolveWorkingDirectory(inspection, gameId, out var workingDirectory, out var rejection))
        {
            return rejection!;
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

            return Reject(
                ErrorCatalog.GameLaunchProcessFailedCode,
                $"El proceso del juego '{gameId}' no arrancó a partir del ejecutable '{executablePath}'. No se ha quedado ningún proceso en marcha.");
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
            rejection = Reject(
                ErrorCatalog.GameLaunchPathOutsideRootCode,
                $"El directorio de trabajo '{exception.RelativePath}' del juego '{gameId}' resuelve en '{exception.CanonicalPath}', que está fuera de la raíz de instalación '{exception.RootPath}'. No se ejecuta ningún proceso.");

            workingDirectory = string.Empty;

            return false;
        }
    }

    /// <summary>
    /// Builds the rejection of a launch, with its cause and the exact path when the
    /// problem is a path (AC1, AD-15, AD-25).
    /// </summary>
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <returns>The outcome of the rejection.</returns>
    private GameLaunchOutcome Reject(string code, string reason)
    {
        var error = ErrorCatalog.Resolve(code);

        _logger.LogWarning(
            "El lanzamiento se rechaza con el código {Code}: {Reason}",
            code,
            reason);

        return GameLaunchOutcome.Rejected(error, reason);
    }
}
