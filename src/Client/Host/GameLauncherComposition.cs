using LANPartyHub.Client.Application.Diagnostics;
using LANPartyHub.Client.Application.GameLauncher;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Infrastructure.Install;
using LANPartyHub.Client.Infrastructure.Process;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

/// <summary>
/// Composition of the launcher of the videogames of the client (AD-19, FR-9, AC1, AC2,
/// AC5).
///
/// <para>
/// La composición entrega cada PUERTO con su única implementación y construye el caso de
/// uso del lanzamiento, así que el módulo <c>GameLauncher</c> no depende de la
/// plataforma (AD-1, AD-19).
/// </para>
/// <para>
/// El puerto de la INSPECCIÓN se entrega con el adaptador que lee la instalación
/// registrada, el manifiesto FIRMADO de la caché y certifica el árbol, y el puerto de la
/// EJECUCIÓN se entrega con el adaptador que arranca el juego como proceso propio
/// (AD-19, AC1, AC3).
/// </para>
/// <para>
/// El estado de ejecución vive EN MEMORIA del cliente: no se persiste, no hay tabla
/// nueva ni migración, y el cliente NO es dueño del ciclo de vida del juego (AD-22,
/// AC2).
/// </para>
/// <para>
/// La CONEXIÓN DIRECTA a servidores de juego y los LAUNCHERS EXTERNOS quedan fuera del
/// MVP: la composición no expone ninguna vía para ellos, así que la extensión futura no
/// se implementa antes de tiempo (AD-19, AC5).
/// </para>
/// </summary>
public sealed class GameLauncherComposition
{
    private readonly IGameProcessRunner _runner;
    private readonly IInstallationInspector _inspector;
    private readonly GameLaunchService _service;
    private readonly PlayabilityProjection _projection;
    private readonly IPlayabilityIntentPort _intents;
    private readonly PlayabilityIntentService _intentService;

    /// <summary>
    /// Creates the composition of the launcher of the videogames.
    /// </summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="snapshots">Port of the persistence of the snapshot of the catalog.</param>
    /// <param name="manifestCache">Port of the cache of the signed manifests.</param>
    /// <param name="paths">Resolver of the paths of the installation, which confines them (AD-9).</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="errorRecorder">
    /// Use case that REGISTERS the errors del lanzamiento, or <c>null</c> when the client
    /// does not register them. Sin registrador el lanzamiento SIGUE: el diagnóstico es un
    /// complemento y no su motivo de vida (AD-1, AD-11, NFR-7).
    /// </param>
    public GameLauncherComposition(
        IClientStateStore stateStore,
        ICatalogSnapshotStore snapshots,
        IManifestCache manifestCache,
        IInstallPathResolver paths,
        ILoggerFactory loggerFactory,
        RecordClientError? errorRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(manifestCache);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // AD-19, AC3: el juego arranca como proceso propio, fuera de la ventana del
        // cliente, y el cliente no captura teclas.
        _runner = new SystemGameProcessRunner(
            loggerFactory.CreateLogger<SystemGameProcessRunner>());

        // AD-2, AC5: la inspección lee el MANIFIESTO FIRMADO y la certificación es la
        // única definición de «instalación válida».
        _inspector = new InstallationInspector(
            stateStore,
            snapshots,
            manifestCache,
            paths,
            loggerFactory.CreateLogger<InstallationInspector>());

        _service = new GameLaunchService(
            _inspector,
            paths,
            _runner,
            loggerFactory.CreateLogger<GameLaunchService>(),
            errorRecorder);

        // AC1, AC2: la proyección de la jugabilidad REderiva la decisión al pintar, a
        // partir del veredicto de la certificación y del inspector, así que no hay columna
        // nueva ni migración (AD-11, AD-22).
        //
        // AC1, AC3: la proyección lee la VERSIÓN PUBLICADA del snapshot LOCAL que el
        // cliente ya sincronizó, así que la desactualización se deriva sin ir al servidor
        // (AD-5, AD-26).
        _projection = new PlayabilityProjection(stateStore, _inspector, snapshots);

        // AC1, AC2: las primarias y la secundaria son SEAMS: el puerto REGISTRA la
        // intención y la ejecución vive en 8.3, en 5.x/6.x y en el registro de
        // diagnóstico.
        _intents = new LoggingPlayabilityIntentPort(
            loggerFactory.CreateLogger<LoggingPlayabilityIntentPort>());

        // AC1, AC2: la regla de qué intención corresponde a qué acción primaria vive en
        // la CAPA DE APLICACIÓN, así que la composición sólo la entrega (AD-1).
        _intentService = new PlayabilityIntentService(_intents);
    }

    /// <summary>
    /// The case of use of the launch of a videogame (AD-19, AC1).
    /// </summary>
    public GameLaunchService Service => _service;

    /// <summary>
    /// Port of the run of the videogame process, delivered with its only implementation
    /// (AD-19, AC5).
    /// </summary>
    public IGameProcessRunner Runner => _runner;

    /// <summary>
    /// Port of the inspection of the installation, delivered with its only
    /// implementation (AD-19, AC5).
    /// </summary>
    public IInstallationInspector Inspector => _inspector;

    /// <summary>
    /// The projection of the playability of the installations, que REderiva la decisión
    /// al pintar (AC1, AC2, AD-11).
    /// </summary>
    public PlayabilityProjection Projection => _projection;

    /// <summary>
    /// Port of the registry of the intentions of a blocked installation, delivered with
    /// its only implementation (AC1, AC2, AD-1).
    /// </summary>
    public IPlayabilityIntentPort Intents => _intents;

    /// <summary>
    /// The case of use that registers the intentions of a blocked installation, que
    /// ENRUTA la acción primaria polimórfica a la intención que le corresponde (AC1,
    /// AC2, AD-1).
    /// </summary>
    public PlayabilityIntentService IntentService => _intentService;
}
