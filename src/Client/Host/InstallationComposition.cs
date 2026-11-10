using LANPartyHub.Client.Application.CatalogSync;
using LANPartyHub.Client.Application.Install;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Filesystem;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Client.Infrastructure.Trust;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Host;

    /// <summary>
    /// Composition of the installations of the client (AD-7, AD-8, AD-9, AD-10, AD-11,
    /// AD-22, AC1..AC7).
    ///
    /// <para>
    /// La composición entrega cada PUERTO con su única implementación y construye el
    /// coordinador de las instalaciones (AD-1).
    /// </para>
    /// <para>
    /// El puerto de la APLICACIÓN del árbol se entrega con el adaptador que aplica las
    /// operaciones TIPADAS del manifiesto dentro de la raíz configurada, y ese adaptador
    /// repite la comprobación del confine antes de cada escritura (AD-9, AC5).
    /// </para>
    /// <para>
    /// La operación PRIVILEGIADA pide confirmación explícita por el puerto de la
    /// confirmación, y la confirmación vive SÓLO en memoria: al reanudar una operación
    /// tras un reinicio se vuelve a pedir (AC2, AC3).
    /// </para>
    /// </summary>
    public sealed class InstallationComposition : IDisposable
    {
        private readonly HttpClient? _httpClient;

        /// <summary>
        /// Creates the composition of the installations.
        /// </summary>
        /// <param name="manifests">Port of the read of the signed manifest of a package.</param>
        /// <param name="verifier">Port of the verification of the signature of the manifest.</param>
        /// <param name="anchorReader">Port of the read of the ancla de confianza ya fijada.</param>
        /// <param name="manifestCache">Port of the cache of the signed manifests.</param>
        /// <param name="layout">Layout of the data folder of the client.</param>
        /// <param name="stateStore">Access to the database of the client.</param>
        /// <param name="paths">Resolver of the paths of the installation.</param>
        /// <param name="loggerFactory">Factory of the log of the client.</param>
        /// <param name="confirmation">
        /// Port of the confirmation of the privileged operations, or <c>null</c> when the
        /// composition is built without it. Sin confirmación, la operación privilegiada NO
        /// se ejecuta: es la NO ejecución la que protege al jugador (AC2).
        /// </param>
        /// <param name="snapshots">
        /// Port of the persistence of the snapshot of the catalog, que es donde se resuelve
        /// el paquete de la VERSIÓN INSTALADA que se repara (AC1).
        /// </param>
        /// <param name="httpClient">
        /// Client of the server of the event, when the composition OWNS it, or <c>null</c>
        /// when the caller does.
        /// </param>
        /// <param name="progressReporter">
        /// Port of the publication of the progress of the installation, or <c>null</c>
        /// when the composition cannot publish it. Sin publicador la instalación SIGUE:
        /// la proyección es un complemento y no su motivo de vida (AD-1, AD-22).
        /// </param>
        public InstallationComposition(
            ISignedManifestClient manifests,
            IManifestSignatureVerifier verifier,
            ITrustAnchorReader anchorReader,
            IManifestCache manifestCache,
            IClientDataLayout layout,
            IClientStateStore stateStore,
            IInstallPathResolver paths,
            ILoggerFactory loggerFactory,
            IPrivilegedOperationConfirmation? confirmation = null,
            ICatalogSnapshotStore? snapshots = null,
            HttpClient? httpClient = null,
            IInstallationProgressReporter? progressReporter = null)
        {
            ArgumentNullException.ThrowIfNull(manifests);
            ArgumentNullException.ThrowIfNull(verifier);
            ArgumentNullException.ThrowIfNull(anchorReader);
            ArgumentNullException.ThrowIfNull(manifestCache);
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(stateStore);
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _httpClient = httpClient;

            Paths = paths;

            // AD-7, AC2: el ejecutor aplica las seis operaciones tipadas, pide la
            // confirmación de la privilegiada y publica el progreso con los bytes REALES.
            var extractor = new ZipInstallArchiveExtractor(paths);

            Confirmation = confirmation ?? new DecliningPrivilegedOperationConfirmation();

            Executor = new InstallOperationExecutor(
                paths,
                extractor,
                Confirmation,
                loggerFactory.CreateLogger<InstallOperationExecutor>());

            Tree = new ManifestTreeWriter(
                paths,
                Executor,
                loggerFactory.CreateLogger<ManifestTreeWriter>());

            // AD-22: el tracker de las instalaciones EN CURSO es el dueño del
            // identificador de la operación y de la fase REAL que se publica al
            // servidor. Sin publicador la instalación SIGUE, así que el tracker se
            // construye aunque no haya a quién publicar (AD-1, AD-22).
            ActiveInstallations = new ActiveInstallations(
                progressReporter,
                loggerFactory.CreateLogger<ActiveInstallations>());

            // AC1, UX-DR-20: la REPARACIÓN reutiliza el mismo ejecutor y el mismo
            // confine que la instalación, y su caso de uso es de CERO RED: localiza el
            // paquete YA VALIDADO en la carpeta de descargas y el manifiesto FIRMADO en
            // la caché (AC1, AD-9).
            //
            // Sin el snapshot del catálogo la reparación NO puede resolver el paquete de
            // la versión instalada, así que el caso de uso queda SIN resolver y la
            // instalación no es reparable (AC1).
            RepairService = new InstallationRepairService(
                new ValidatedPackageStore(layout),
                manifestCache,
                snapshots ?? EmptyCatalogSnapshotStore.Instance,
                paths,
                Tree,
                stateStore,
                loggerFactory.CreateLogger<InstallationRepairService>());

            Coordinator = new InstallationCoordinator(
                manifests,
                verifier,
                anchorReader,
                manifestCache,
                layout,
                paths,
                Tree,
                stateStore,
                loggerFactory.CreateLogger<InstallationCoordinator>(),
                ActiveInstallations);
        }

        /// <summary>
        /// Resolver of the paths of the installation, delivered with its only
        /// implementation (AD-9).
        /// </summary>
        public IInstallPathResolver Paths { get; }

        /// <summary>
        /// The executor of the typed operations of the manifest, que es el que pide la
        /// confirmación de lo privilegiado y publica el progreso (AD-7, AC2, AC4).
        /// </summary>
        public InstallOperationExecutor Executor { get; }

        /// <summary>
        /// Port of the application of the typed operations of the manifest, delivered with
        /// its only implementation (AD-7, AD-9).
        /// </summary>
        public IInstallTreeWriter Tree { get; }

        /// <summary>
        /// Port of the confirmation of the privileged operations, que es el que decide si
        /// la operación se ejecuta (AC2).
        /// </summary>
        public IPrivilegedOperationConfirmation Confirmation { get; }

    /// <summary>
    /// The coordinator of the installation operations of the client.
    /// </summary>
    public InstallationCoordinator Coordinator { get; }

    /// <summary>
    /// Tracker of the INSTALLATION operations that the client is running right now, que
    /// es el dueño del identificador de la operación y de la fase REAL que se publica
    /// al servidor (AD-22, FR-10).
    /// </summary>
    public ActiveInstallations ActiveInstallations { get; }

    /// <summary>
    /// The case of use of the REPAIR of an invalid installation, que reutiliza el paquete
    /// ya validado SIN volver a descargar (AC1, UX-DR-20).
    /// </summary>
    public InstallationRepairService RepairService { get; }

    /// <summary>
    /// Builds the composition of the installations over the client HTTP surface of the
    /// event server, with the anchor reader that the store of the client is (AD-2,
    /// AD-27).
    ///
    /// <para>
    /// La composición abre SU PROPIO cliente HTTP sobre la superficie del cliente, con el
    /// manejador de cabeceras instalado, así que toda petición declara la versión del
    /// cliente y la del protocolo (AD-6, AD-15, AD-23).
    /// </para>
    /// </summary>
    /// <param name="serverAddress">Absolute address of the event server.</param>
    /// <param name="headers">Source of the identifiers that the requests carry.</param>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="trustAnchorReader">Port of the read of the ancla de confianza ya fijada.</param>
    /// <param name="loggerFactory">Factory of the log of the client.</param>
    /// <param name="confirmation">
    /// Port of the confirmation of the privileged operations, or <c>null</c> when the
    /// composition is built without interfaz. Sin confirmación, la operación privilegiada
    /// NO se ejecuta (AC2).
    /// </param>
    /// <param name="snapshots">
    /// Port of the persistence of the snapshot of the catalog, que es donde se resuelve el
    /// paquete de la VERSIÓN INSTALADA que se repara (AC1).
    /// </param>
    /// <param name="progressReporter">
    /// Port of the publication of the progress of the installation, or <c>null</c> when
    /// the composition cannot publish it. Sin publicador la instalación SIGUE: la
    /// proyección es un complemento y no su motivo de vida (AD-1, AD-22).
    /// </param>
    /// <returns>The composition of the installations.</returns>
    public static InstallationComposition Create(
        Uri serverAddress,
        IClientRequestHeaders headers,
        IClientDataLayout layout,
        IClientStateStore stateStore,
        ITrustAnchorReader trustAnchorReader,
        ILoggerFactory loggerFactory,
        IPrivilegedOperationConfirmation? confirmation = null,
        ICatalogSnapshotStore? snapshots = null,
        IInstallationProgressReporter? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(trustAnchorReader);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // AD-6: la versión del cliente es la del binario que se está ejecutando y la
        // versión del protocolo es la constante del contrato compartido.
        var versions = new ClientBuildVersion();

        var httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, versions)
            {
                InnerHandler = new HttpClientHandler(),
            })
        {
            BaseAddress = serverAddress,
        };

        return new InstallationComposition(
            new SignedManifestHttpClient(httpClient),
            new Ed25519ManifestVerifier(),
            trustAnchorReader,
            new ManifestCache(layout),
            layout,
            stateStore,
            new InstallPathResolver(layout),
            loggerFactory,
            confirmation,
            snapshots,
            httpClient,
            progressReporter);
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient?.Dispose();
}

/// <summary>
/// Adapter of the application of the typed operations of the manifest over the
/// installation root (AD-7, AD-9, AC5).
///
/// <para>
/// Las operaciones del manifiesto son TIPADAS y CERRADAS, y este adaptador es el que las
/// aplica. Cada escritura se resuelve contra la raíz configurada y la ruta canónica
/// resultante se COMPRUEBA antes de la escritura, no sólo al principio (AD-9).
/// </para>
/// <para>
/// El adaptador NO decide el estado de la instalación: devuelve lo que el árbol permite
/// afirmar y el caso de uso lo certifica (AC5).
/// </para>
/// </summary>
public sealed class ManifestTreeWriter : IInstallTreeWriter
{
    private readonly IInstallPathResolver _paths;
    private readonly InstallOperationExecutor? _executor;
    private readonly ILogger<ManifestTreeWriter> _logger;

    /// <summary>Creates the adapter of the application of the typed operations.</summary>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <param name="logger">Log of the adapter.</param>
    public ManifestTreeWriter(IInstallPathResolver paths, ILogger<ManifestTreeWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Creates the adapter over the EXECUTOR of the typed operations, que es el que pide
    /// la confirmación de lo privilegiado y publica el progreso (AD-7, AC2, AC4).
    /// </summary>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <param name="executor">Executor of the typed operations of the manifest.</param>
    /// <param name="logger">Log of the adapter.</param>
    public ManifestTreeWriter(
        IInstallPathResolver paths,
        InstallOperationExecutor executor,
        ILogger<ManifestTreeWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<InstallTreeOutcome> ApplyAsync(
        InstallationManifest manifest,
        string rootPath,
        string? packagePath,
        CancellationToken cancellationToken) =>
        ApplyAsync(manifest, rootPath, packagePath, new DiscardingProgressSink(), cancellationToken);

    /// <inheritdoc />
    public Task<InstallTreeOutcome> ApplyAsync(
        InstallationManifest manifest,
        string rootPath,
        string? packagePath,
        IInstallProgressSink progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(progress);

        // AD-7, AC2, AC4: con el EXECUTOR, la operación privilegiada pide confirmación
        // explícita y el progreso se publica con la fase y los bytes REALES.
        if (_executor is not null)
        {
            return _executor.ApplyAsync(manifest, rootPath, packagePath, progress, cancellationToken);
        }

        return ApplyLegacyAsync(manifest, rootPath, packagePath, cancellationToken);
    }

    /// <summary>
    /// Sumidero que IGNORA el progreso, para la sobrecarga que no publica (AD-1).
    /// </summary>
    private sealed class DiscardingProgressSink : IInstallProgressSink
    {
        public void Report(InstallPhase phase, long bytesWritten, long? totalBytes)
        {
            // La sobrecarga sin progreso no publica nada: el sumidero existe para que el
            // ejecutor no tenga que conocer la interfaz (AD-1).
        }
    }

    /// <summary>
    /// Applies the typed operations of the manifest WITHOUT the executor, que es el
    /// camino que usan los dobles de los puertos en las pruebas de 7.1 (AD-7, AD-9, AC5).
    /// </summary>
    /// <param name="manifest">Manifest whose operations are applied.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <param name="packagePath">Absolute path of the file that holds the bytes of the package.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the application of the tree.</returns>
    private async Task<InstallTreeOutcome> ApplyLegacyAsync(
        InstallationManifest manifest,
        string rootPath,
        string? packagePath,
        CancellationToken cancellationToken)
    {
        var verifyPassed = true;
        var pathsConfinedToRoot = true;
        var operationNotAllowed = false;
        string? rejectedPath = null;

        // AD-9, AC2: el manifiesto se EVALÚA ENTERO antes de la primera escritura. Una
        // ruta absoluta o una ruta que sale de la raíz se rechaza aquí, así que el
        // rechazo PRECEDE a persistir y no deja ni un directorio ni un archivo creados
        // (AD-27).
        try
        {
            ManifestPathPolicy.Evaluate(manifest);
        }
        catch (PathEscapeException exception)
        {
            // La verificación NO se ejecutó: el manifiesto se rechazó antes de la primera
            // escritura, así que la operación de `verify` no llegó a comprobar nada y no
            // puede afirmarse que PASÓ (AC5).
            verifyPassed = false;
            pathsConfinedToRoot = false;
            rejectedPath = exception.RelativePath;

            // El diagnóstico lleva el `ApiError` COMPLETO del catálogo, que es el sumidero
            // durable del rechazo (AD-15, AD-25).
            _logger.LogWarning(
                exception,
                "La operación '{Operation}' del manifiesto declara la ruta '{RelativePath}', que no se admite: la escritura se rechaza con el código {Code}. {Message} {Cause} {NextStep}",
                exception.Operation ?? "desconocida",
                exception.RelativePath,
                exception.Cause.Code,
                exception.Cause.Message,
                exception.Cause.Cause,
                exception.Cause.NextStep);

            return new InstallTreeOutcome(verifyPassed, pathsConfinedToRoot, rejectedPath);
        }

        foreach (var operation in manifest.Operations)
        {
            // AD-9: la comprobación del confine se REPITE antes de cada escritura, así
            // que una ruta que sale de la raíz se rechaza en el momento de escribirla.
            switch (operation)
            {
                case InstallationManifest.CreateDirectory createDirectory:
                    if (!TryResolve(createDirectory.Path, out var directoryPath, out var directoryRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= directoryRejection;

                        continue;
                    }

                    Directory.CreateDirectory(directoryPath);

                    break;

                case InstallationManifest.WriteConfig writeConfig:
                    if (!TryResolve(writeConfig.Path, out var configPath, out var configRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= configRejection;

                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                    await File.WriteAllTextAsync(configPath, writeConfig.Content, cancellationToken)
                        .ConfigureAwait(false);

                    break;

                case InstallationManifest.Verify verify:
                    if (!TryResolve(verify.Path, out var verifiedPath, out var verifyRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= verifyRejection;

                        continue;
                    }

                    // La operación `verify` del manifiesto comprueba que el archivo
                    // declarado EXISTE dentro de la raíz instalada (AD-7).
                    if (!File.Exists(verifiedPath))
                    {
                        verifyPassed = false;

                        _logger.LogWarning(
                            "La operación de verificación del manifiesto no pasa: '{Path}' no existe dentro de la raíz de instalación.",
                            verify.Path);
                    }

                    break;

                case InstallationManifest.SetLaunchConfig:
                    // La configuración de lanzamiento no escribe en el disco: la
                    // materializa el lanzador, que es otra historia (AD-7).
                    break;

                case InstallationManifest.Extract extract:
                case InstallationManifest.CopyFile:
                    // La extracción y la copia de archivos dentro de la raíz son de la
                    // historia de los confines de la extracción (AD-9).
                    if (!TryResolve(ExtractDestination(operation), out _, out var copyRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= copyRejection;
                    }

                    break;

                default:
                    // AD-7: una operación fuera del conjunto cerrado NUNCA se ejecuta. El
                    // rechazo se registra con su causa (AC2).
                    _logger.LogWarning(
                        "La operación de manifiesto '{Operation}' no pertenece al conjunto cerrado y no se ejecuta.",
                        operation?.GetType().Name ?? "desconocida");

                    operationNotAllowed = true;

                    break;
            }
        }

        // AD-7, AC2: una operación fuera del conjunto cerrado tiene su propia causa, y
        // no es la de una ruta que sale de la raíz (AD-15, AD-25). El árbol NO decide el
        // estado de la instalación: eso es de la CERTIFICACIÓN (AC5).
        return new InstallTreeOutcome(
            verifyPassed,
            pathsConfinedToRoot && !operationNotAllowed,
            rejectedPath);
    }

    /// <summary>
    /// Reads the destination of an operation that copies bytes inside the root (AD-9).
    /// </summary>
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns>The relative path of the destination.</returns>
    private static string ExtractDestination(InstallationManifest.ManifestOperation operation) => operation switch
    {
        InstallationManifest.Extract extract => extract.DestinationPath,
        InstallationManifest.CopyFile copyFile => copyFile.DestinationPath,
        _ => string.Empty,
    };

    /// <summary>
    /// Resolves a relative path inside the root, and tells whether it stayed inside
    /// (AD-9).
    ///
    /// <para>
    /// La comprobación se REPETE antes de cada escritura, así que una ruta que era
    /// válida deja de serlo en cuanto aparece un enlace que sale de la raíz (AD-9).
    /// </para>
    /// </summary>
    /// <param name="relativePath">Relative path of the manifest.</param>
    /// <param name="resolved">Canonical absolute path that resolves inside the root.</param>
    /// <param name="rejection">The path that was rejected, or <c>null</c>.</param>
    /// <returns><c>true</c> when the path stays inside the root.</returns>
    private bool TryResolve(string relativePath, out string resolved, out string? rejection)
    {
        resolved = string.Empty;
        rejection = null;

        try
        {
            resolved = _paths.ResolveInsideRoot(relativePath);

            return true;
        }
        catch (InstallPathOutsideRootException exception)
        {
            rejection = exception.CanonicalPath;

            // El diagnóstico lleva el `ApiError` COMPLETO del catálogo, que es el sumidero
            // durable del rechazo (AD-15, AD-25).
            var cause = ErrorCatalog.Resolve(ErrorCatalog.InstallPathOutsideRootCode);

            _logger.LogWarning(
                exception,
                "La ruta '{RelativePath}' del manifiesto de instalación sale de la raíz configurada: la escritura se rechaza con el código {Code}. {Message} {Cause} {NextStep}",
                exception.RelativePath,
                cause.Code,
                cause.Message,
                cause.Cause,
                cause.NextStep);

            return false;
        }
    }
}
