using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// Port of the application of the typed operations of the manifest over the installation
/// root (AD-7, AD-9, AC5).
///
/// <para>
/// El port existe para que el ORDEN NORMATIVO de la instalación sea observable sin un
/// disco real y sin un motor de extracción: el caso de uso decide CUÁNDO se escribe y
/// el adaptador decide CÓMO (AD-1, AD-16).
/// </para>
/// <para>
/// La aplicación del árbol TIENE que devolver las dos condiciones que la certificación
/// necesita del árbol: si la operación <c>verify</c> del manifiesto pasa sobre el árbol
/// instalado, y si toda ruta escrita quedó dentro de la raíz configurada (AD-7, AD-9,
/// AC5).
/// </para>
/// </summary>
public interface IInstallTreeWriter
{
    /// <summary>
    /// Applies the typed operations of the manifest inside the installation root (AD-7,
    /// AD-9).
    ///
    /// <para>
    /// El port NO decide el estado de la instalación: devuelve lo que el árbol permite
    /// afirmar y el caso de uso lo CERTIFICA (AC5).
    /// </para>
    /// </summary>
    /// <param name="manifest">Manifest whose operations are applied.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <param name="packagePath">
    /// Absolute path of the file that holds the bytes of the validated package, or
    /// <c>null</c> when the package is not available on disk.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the application of the tree.</returns>
    Task<InstallTreeOutcome> ApplyAsync(
        InstallationManifest manifest,
        string rootPath,
        string? packagePath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the typed operations of the manifest inside the installation root, and
    /// PUBLISHES the progress of the operation (AD-7, AD-9, AC4).
    ///
    /// <para>
    /// La publicación del progreso es lo que distingue esta sobrecarga de la anterior: el
    /// ejecutor pide la CONFIRMACIÓN de la operación privilegiada y publica la fase con
    /// el porcentaje de los bytes REALES (AC2, AC4).
    /// </para>
    /// </summary>
    /// <param name="manifest">Manifest whose operations are applied.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <param name="packagePath">
    /// Absolute path of the file that holds the bytes of the validated package, or
    /// <c>null</c> when the package is not available on disk.
    /// </param>
    /// <param name="progress">Port that receives the progress of the operation.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the application of the tree.</returns>
    Task<InstallTreeOutcome> ApplyAsync(
        InstallationManifest manifest,
        string rootPath,
        string? packagePath,
        IInstallProgressSink progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the application of the tree of the installation asserts about the installed
/// files (AD-7, AD-9, AC5).
/// </summary>
/// <param name="VerifyPassed">
/// Whether the <c>verify</c> operation of the manifest passes over the installed tree.
/// </param>
/// <param name="PathsConfinedToRoot">
/// Whether every written path resolved inside the configured installation root.
/// </param>
/// <param name="RejectedPath">
/// The canonical path that was rejected because it escaped the root, or <c>null</c>
/// when there was none.
/// </param>
public sealed record InstallTreeOutcome(
    bool VerifyPassed,
    bool PathsConfinedToRoot,
    string? RejectedPath = null);

/// <summary>
/// Outcome of an installation operation of the client (AD-10, AC1, AC2, AC4).
///
/// <para>
/// El resultado distingue el RECHAZO (que no deja ninguna escritura) del VEREDICTO de una
/// instalación que sí llegó a escribirse (AC2, AC4).
/// </para>
/// </summary>
public sealed record InstallationResult
{
    /// <summary>
    /// The operation was REJECTED before any write, and the error carries its cause
    /// (AC2, AC3).
    /// </summary>
    /// <param name="error">Error of the catalog that explains the rejection.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <returns>The result of the rejection.</returns>
    public static InstallationResult Rejected(ApiError error, string reason) =>
        new() { IsRejected = true, Error = error, Reason = reason };

    /// <summary>
    /// The operation was CERTIFIED, and the verdict says the resulting state (AC4,
    /// AC5).
    /// </summary>
    /// <param name="verdict">Verdict of the certification.</param>
    /// <param name="installDirectoryId">Opaque identifier of the installation root.</param>
    /// <param name="installPath">Local path of the installation root.</param>
    /// <returns>The result of the certification.</returns>
    public static InstallationResult Certified(
        InstallationVerdict verdict,
        string installDirectoryId,
        string installPath) =>
        new()
        {
            IsRejected = false,
            Verdict = verdict,
            InstallDirectoryId = installDirectoryId,
            InstallPath = installPath,
        };

    /// <summary>
    /// The operation was CERTIFIED, and the tree was ALREADY installed with the same
    /// version, así que no se repitió ninguna operación (AC1).
    /// </summary>
    /// <param name="verdict">Verdict of the certification.</param>
    /// <param name="installDirectoryId">Opaque identifier of the installation root.</param>
    /// <param name="installPath">Local path of the installation root.</param>
    /// <returns>The result of the certification.</returns>
    public static InstallationResult AlreadyInstalled(
        InstallationVerdict verdict,
        string installDirectoryId,
        string installPath) =>
        new()
        {
            IsRejected = false,
            Verdict = verdict,
            InstallDirectoryId = installDirectoryId,
            InstallPath = installPath,
            IsAlreadyInstalled = true,
        };

    /// <summary>Whether the operation was rejected before writing anything (AC2).</summary>
    public bool IsRejected { get; init; }

    /// <summary>Whether the operation reached a certified state (AC4).</summary>
    public bool IsCertified => !IsRejected;

    /// <summary>
    /// Whether the tree was ALREADY installed with the same version, así que la
    /// operación NO repitió ninguna escritura (AC1).
    /// </summary>
    public bool IsAlreadyInstalled { get; init; }

    /// <summary>
    /// Error of the catalog that explains the rejection, or <c>null</c> when the
    /// operation was certified.
    /// </summary>
    public ApiError? Error { get; init; }

    /// <summary>
    /// Human readable reason of the rejection, or <c>null</c> when the operation was
    /// certified.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Verdict of the certification, or <c>null</c> when the operation was rejected.
    /// </summary>
    public InstallationVerdict? Verdict { get; init; }

    /// <summary>
    /// Opaque identifier of the installation root, or <c>null</c> when the operation was
    /// rejected (AC1).
    /// </summary>
    public string? InstallDirectoryId { get; init; }

    /// <summary>
    /// Local path of the installation root, or <c>null</c> when the operation was
    /// rejected. It never leaves the client (AD-22).
    /// </summary>
    public string? InstallPath { get; init; }
}

/// <summary>
/// The operation of an installation that FAILED, and the data that «Reintentar» needs to
/// repeat it (AC5, NFR-9, UX-DR-34).
///
/// <param name="GameId">Identifier of the videogame that is installed.</param>
/// <param name="PackageId">Opaque identifier of the package that was being installed.</param>
/// <param name="Version">Version of the videogame that is installed.</param>
/// <param name="ErrorCode">Stable code of the error of the catalog that explains the failure.</param>
public sealed record FailedInstallation(
    string GameId,
    string PackageId,
    string Version,
    string ErrorCode);

/// <summary>
/// Coordinator of the installation operations of the client (AD-7, AD-8, AD-9, AD-10,
/// AD-11, AD-16, AD-22, AC1..AC7).
///
/// <para>
/// El coordinador es el ÚNICO dueño del ORDEN NORMATIVO de la instalación: (1) obtener el
/// manifiesto firmado; (2) verificar la firma y la versión del esquema; (3) SOLO ENTONCES
/// crear el directorio de instalación y el archivo parcial; (4) descargar el contenido;
/// (5) calcular el <c>sha256</c> y compararlo; (6) si no coincide, descartar el parcial
/// y marcar la operación como fallida (AD-8, AC1).
/// </para>
/// <para>
/// NINGÚN byte se escribe en disco antes del paso 2: un manifiesto que no valida contra
/// el ancla fijada no deja manifiesto en la caché <c>manifests/</c>, ni archivo parcial,
/// ni registro de operación (AD-8, AC3, AD-27).
/// </para>
/// <para>
/// El veredicto lo produce la CERTIFICACIÓN, que es una regla pura del dominio: las tres
/// condiciones y la reparabilidad se deciden en un solo sitio, así que no existe una
/// segunda definición de «instalación válida» (AC5, AC6, AC7).
/// </para>
/// <para>
/// El coordinador depende de los PUERTOS y nunca de un adaptador, así que el orden de
/// rechazo y la ausencia de escrituras son observables sin un servidor y sin un disco
/// (AD-1, AD-16).
/// </para>
/// </summary>
public sealed class InstallationCoordinator
{
    private readonly ISignedManifestClient _manifests;
    private readonly IManifestSignatureVerifier _verifier;
    private readonly ITrustAnchorReader _anchorReader;
    private readonly IManifestCache _manifestCache;
    private readonly IClientDataLayout _layout;
    private readonly IInstallPathResolver _paths;
    private readonly IInstallTreeWriter _tree;
    private readonly IClientStateStore _stateStore;
    private readonly ActiveInstallations? _activeInstallations;
    private readonly ILogger<InstallationCoordinator> _logger;

    /// <summary>Raised when the progress of an installation changes (AC4).</summary>
    public event Action<InstallProgress>? ProgressChanged;

    /// <summary>
    /// The operation that FAILED, así que la acción «Reintentar» sabe QUÉ repetir sin
    /// reinstalar el cliente (AC5, NFR-9, UX-DR-34).
    ///
    /// <para>
    /// El dato vive en el COORDINADOR porque es el dueño del orden normativo y de la
    /// identidad de la operación: la vista sólo lo presenta (AD-1).
    /// </para>
    /// </summary>
    public FailedInstallation? FailedOperation { get; private set; }

    /// <summary>Creates the coordinator of the installations.</summary>
    /// <param name="manifests">Port of the read of the signed manifest of a package.</param>
    /// <param name="verifier">Port of the verification of the signature of the manifest.</param>
    /// <param name="anchorReader">Port of the read of the ancla de confianza ya fijada.</param>
    /// <param name="manifestCache">Port of the cache of the signed manifests.</param>
    /// <param name="layout">Layout of the data folder of the client.</param>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <param name="tree">Port of the application of the typed operations of the manifest.</param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="logger">Log of the coordinator.</param>
    /// <param name="activeInstallations">
    /// Tracker of the INSTALLATION operations in course, or <c>null</c> when the client
    /// does not track them. Con tracker, la FASE REAL de la operación se publica al
    /// servidor y la tabla del organizador la muestra (AD-22, FR-10).
    /// </param>
    public InstallationCoordinator(
        ISignedManifestClient manifests,
        IManifestSignatureVerifier verifier,
        ITrustAnchorReader anchorReader,
        IManifestCache manifestCache,
        IClientDataLayout layout,
        IInstallPathResolver paths,
        IInstallTreeWriter tree,
        IClientStateStore stateStore,
        ILogger<InstallationCoordinator> logger,
        ActiveInstallations? activeInstallations = null)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(anchorReader);
        ArgumentNullException.ThrowIfNull(manifestCache);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(logger);

        _manifests = manifests;
        _verifier = verifier;
        _anchorReader = anchorReader;
        _manifestCache = manifestCache;
        _layout = layout;
        _paths = paths;
        _tree = tree;
        _stateStore = stateStore;
        _logger = logger;
        _activeInstallations = activeInstallations;
    }

    /// <summary>
    /// Runs an installation in the NORMATIVE order and certifies its outcome (AD-8, AC1,
    /// AC2, AC4, AC5).
    ///
    /// <param name="gameId">Identifier of the videogame that is installed.</param>
    /// <param name="packageId">Opaque identifier of the package that is installed.</param>
    /// <param name="installedVersion">Version of the videogame that is installed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the installation.</returns>
    public async Task<InstallationResult> InstallAsync(
        string gameId,
        string packageId,
        string installedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedVersion);

        // AD-22: la operación de instalación es una UNIDAD DE TRABAJO EN CURSO, así que
        // el cliente la declara al servidor con su identificador y su fase REAL. El
        // identificador lo MIENTE el tracker, que es su dueño (AD-23).
        var trackedOperation = await BeginTrackedOperationAsync(gameId, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await RunInstallationAsync(
                gameId,
                packageId,
                installedVersion,
                trackedOperation,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // AD-22: la operación deja de estar EN CURSO en CUALQUIER salida —cancelación,
            // excepción inesperada o resultado— así que el servidor no la muestra como
            // activa para siempre. Su INSTALACIÓN persiste, que es otra cosa (AD-26).
            //
            // AD-22: el CIERRE se publica con un token VIVO, así que una operación
            // cancelada también llega a soltar su vista en el servidor (AD-22, FR-10).
            await FinishTrackedOperationAsync(trackedOperation).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the installation itself, con la operación YA declarada (AD-22).
    ///
    /// <para>
    /// El seguimiento de la operación lo cierra el <see cref="InstallAsync"/> que la
    /// empezó, así que esta método NO lo cierra: una sola puerta, todos los caminos
    /// (AD-22).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is installed.</param>
    /// <param name="packageId">Opaque identifier of the package that is installed.</param>
    /// <param name="installedVersion">Version of the videogame that is installed.</param>
    /// <param name="trackedOperation">
    /// Identifier of the TRACKED operation, or <c>null</c> when the client does not track
    /// the installation (AD-22).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the installation.</returns>
    private async Task<InstallationResult> RunInstallationAsync(
        string gameId,
        string packageId,
        string installedVersion,
        string? trackedOperation,
        CancellationToken cancellationToken)
    {
        // PASO 1: obtener el manifiesto firmado (AD-8).
        var envelope = await _manifests
            .GetAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        if (envelope is null)
        {
            await PublishFailurePhaseAsync(trackedOperation).ConfigureAwait(false);

            return Reject(
                ErrorCatalog.ManifestSignatureInvalidCode,
                $"El servidor no sirvió un manifiesto firmado para el paquete '{packageId}'.",
                packageId);
        }

        // PASO 2: verificar la firma y la versión del esquema (AD-8).
        var verification = await VerifySignedManifestAsync(packageId, envelope, cancellationToken)
            .ConfigureAwait(false);

        if (verification.Error is not null)
        {
            // El rechazo PRECEDE a persistir: ni manifiesto en la caché, ni directorio de
            // instalación, ni parcial, ni fila (AD-8, AC3, AD-27).
            await PublishFailurePhaseAsync(trackedOperation).ConfigureAwait(false);

            return Reject(
                verification.Error.Code,
                verification.Reason!,
                packageId);
        }

        var manifest = verification.Manifest!;

        // PASO 3: SOLO ENTONCES se crean el directorio de instalación y el archivo
        // parcial (AD-8, AC1, AC3).
        var rootPath = _paths.RootPath;

        Directory.CreateDirectory(rootPath);

        var packagePath = _layout.PartialFilePath(packageId);

        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);

        if (!File.Exists(packagePath))
        {
            await File.WriteAllBytesAsync(packagePath, [], cancellationToken).ConfigureAwait(false);
        }

        // El manifiesto de fiar se cachea DESPUÉS de que la firma haya validado, así que la
        // caché de `manifests/` solo guarda manifiestos de fiar (AD-2, AD-9).
        await _manifestCache
            .StoreAsync(packageId, envelope.Manifest, cancellationToken)
            .ConfigureAwait(false);

        // PASO 4 y 5: la validación de la INTEGRIDAD de los bytes contra el hash que
        // declara el manifiesto FIRMADO (AD-8, AC1).
        var integrity = await ValidateIntegrityAsync(packageId, manifest, packagePath, cancellationToken)
            .ConfigureAwait(false);

        if (!integrity.IsValid)
        {
            // PASO 6: el sha256 NO coincide, así que el parcial se DESCARTA y la
            // operación queda FALLIDA, no reparable por reanudación (AD-8, AC1).
            DiscardPartial(packagePath);

            await PublishFailurePhaseAsync(trackedOperation).ConfigureAwait(false);

            return Reject(
                integrity.Error!.Code,
                $"{integrity.Reason} El archivo parcial se descarta y la operación no se puede reanudar.",
                packageId);
        }

        // AC1: una instalación YA VÁLIDA de la MISMA versión NO repite operaciones. La
        // certificación previa decide si el árbol ya está bien, y sólo entonces se
        // escriben las operaciones del manifiesto (AC1).
        var existing = await CertifyExistingAsync(gameId, manifest, rootPath, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogInformation(
                "La instalación del juego {GameId} ya está certificada con la versión {Version}: no se repite ninguna operación.",
                gameId,
                installedVersion);

            return InstallationResult.AlreadyInstalled(
                existing,
                InstallationCertification.DeriveInstallDirectoryId(rootPath),
                rootPath);
        }

        // La operación pasa por `ValidatingInstallation` antes de terminar en
        // `Installed` o en `Repairable` (AD-10, AC4).
        var machine = new InstallationStateMachine(InstallationState.Pending);

        machine.TryTransitionTo(InstallationState.Installing);

        // AC4: el progreso se publica con la fase y el porcentaje entero de los bytes
        // REALES escritos, así que la barra nunca contradice al texto (UX-DR-13).
        //
        // AD-22: el sumidero publica ALSO la FASE REAL de la operación al servidor, con
        // el identificador que el tracker minte, así que la tabla del organizador
        // muestra la fase en la que está la instalación (AD-22, FR-10).
        var sink = new CoordinatorProgressSink(this, trackedOperation, cancellationToken);

        // PASO 4 (continuación): la aplicación del árbol por puerto (AD-7, AD-9). La
        // operación privilegiada pide CONFIRMACIÓN EXPLÍCITA dentro de esta llamada, y
        // la máquina pasa por `AwaitingConfirmation` mientras la espera (AC2, AD-10).
        InstallTreeOutcome tree;

        try
        {
            try
            {
                tree = await _tree
                    .ApplyAsync(manifest, rootPath, packagePath, sink, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                // Esperar la fase antes del resultado terminal, incluso si falla el árbol.
                await sink.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (PrivilegedOperationDeclinedException exception)
        {
            // AC2, AC5: el decline NO es una escritura. La operación queda FALLIDA con su
            // error PERSISTIDO, así que la acción «Reintentar» está disponible sin
            // reinstalar el cliente (NFR-9).
            machine.TryTransitionTo(InstallationState.AwaitingConfirmation);
            machine.TryTransitionTo(InstallationState.Installing);
            machine.TryTransitionTo(InstallationState.Failed);

            await PersistFailureAsync(
                gameId,
                installedVersion,
                rootPath,
                ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
                exception.Message,
                cancellationToken).ConfigureAwait(false);

            FailedOperation = new FailedInstallation(
                gameId,
                packageId,
                installedVersion,
                ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode);

            // AD-22: la fase REAL de la FALLA se publica antes de cerrar el seguimiento,
            // así que el servidor sabe que la operación terminó (AD-22, FR-10).
            await PublishAsync(
                InstallPhase.Failed,
                sink.LastBytesWritten,
                sink.LastTotalBytes,
                trackedOperation,
                cancellationToken).ConfigureAwait(false);

            return Reject(
                ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
                exception.Message,
                packageId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // AC5: el fallo de la aplicación del árbol deja la operación FALLIDA con su
            // error PERSISTIDO, y la operación es REINTENTABLE sobre el paquete validado
            // (NFR-9).
            machine.TryTransitionTo(InstallationState.Failed);

            await PersistFailureAsync(
                gameId,
                installedVersion,
                rootPath,
                ErrorCatalog.InstallOperationFailedCode,
                exception.Message,
                cancellationToken).ConfigureAwait(false);

            FailedOperation = new FailedInstallation(
                gameId,
                packageId,
                installedVersion,
                ErrorCatalog.InstallOperationFailedCode);

            _logger.LogError(
                exception,
                "La instalación del juego {GameId} falló al aplicar el manifiesto: la operación queda fallida y es reintentable.",
                gameId);

            // AD-22: la fase REAL de la FALLA se publica antes de cerrar el seguimiento,
            // así que el servidor sabe que la operación terminó (AD-22, FR-10).
            await PublishAsync(
                InstallPhase.Failed,
                sink.LastBytesWritten,
                sink.LastTotalBytes,
                trackedOperation,
                cancellationToken).ConfigureAwait(false);

            return Reject(
                ErrorCatalog.InstallOperationFailedCode,
                $"La instalación del juego '{gameId}' falló al aplicar el manifiesto: {exception.Message}",
                packageId);
        }

        // AD-9: la ruta que salió de la raíz se registra con su forma CANÓNICA, que es
        // la que de verdad intentó salirse (AD-9, AC6).
        if (tree.RejectedPath is not null)
        {
            _logger.LogWarning(
                "La instalación del juego {GameId} escribió una ruta que resuelve en '{RejectedPath}', fuera de la raíz de instalación '{RootPath}'.",
                gameId,
                tree.RejectedPath,
                rootPath);
        }

        // AC5: la certificación es la ÚNICA definición de instalación válida. Las tres
        // condiciones se evaluan JUNTAS y ninguna sustituye a otra.
        //
        // La primera condición se afirma porque el paquete se validó contra el ancla
        // FIJADA en el paso 2 y su sha256 coincidió en el paso 5, y la reparabilidad se
        // afirma porque el paquete validado SIGUE disponible con su hash coincidente
        // (AD-2, AD-8, AC6).
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: tree.VerifyPassed,
            pathsConfinedToRoot: tree.PathsConfinedToRoot,
            repairablePackageAvailable: true);

        machine.TryTransitionTo(InstallationState.ValidatingInstallation);
        machine.TryTransitionTo(verdict.State);

        // El identificador de la raíz es ESTABLE: se deriva de su ruta canónica, así que
        // no cambia ante una reparación ni ante la reinstalación del binario (AC1).
        var installDirectoryId = InstallationCertification.DeriveInstallDirectoryId(rootPath);

        await _stateStore
            .SaveInstallationAsync(
                new ClientInstallation(
                    gameId,
                    installedVersion,
                    verdict.State,
                    rootPath,
                    installDirectoryId),
                cancellationToken)
            .ConfigureAwait(false);

        // AC4: la operación terminada publica su fase final, y la barra conserva su
        // longitud REAL (UX-DR-13).
        //
        // AD-22: la fase final se publica ALSO al servidor, con el identificador de la
        // operación, antes de cerrar el seguimiento (AD-22, FR-10).
        await PublishAsync(
            verdict.IsInstalled ? InstallPhase.Completed : InstallPhase.Failed,
            sink.LastBytesWritten,
            sink.LastTotalBytes,
            trackedOperation,
            cancellationToken).ConfigureAwait(false);

        // AC6: el motivo de la reparabilidad y la acción quedan REGISTRADOS, y el
        // veredicto los lleva de forma VISIBLE (la microcopia del motivo llega en 8.2).
        if (verdict.RepairAction is not null)
        {
            _logger.LogWarning(
                "La instalación del juego {GameId} es reparable: {Reason} La acción de reparación es '{RepairAction}'.",
                gameId,
                verdict.ReasonText,
                verdict.RepairAction);
        }
        else if (!verdict.IsInstalled)
        {
            _logger.LogWarning(
                "La instalación del juego {GameId} NO es reparable: {Reason}",
                gameId,
                verdict.ReasonText);
        }
        else
        {
            _logger.LogInformation(
                "La instalación del juego {GameId} está certificada como válida con el identificador de raíz {InstallDirectoryId}.",
                gameId,
                installDirectoryId);
        }

        // AD-22: la operación deja de estar EN CURSO, así que deja de publicarse como
        // activa. Su INSTALACIÓN persiste, que es otra cosa (AD-22, AD-26). El cierre
        // del seguimiento lo hace la puerta que la empezó (AD-22).
        return InstallationResult.Certified(verdict, installDirectoryId, rootPath);
    }

    /// <summary>
    /// REINTENTA an installation that FAILED, reusing the package that was already
    /// validated (AC5, NFR-9).
    ///
    /// <para>
    /// «Reintentar» es una operación NUEVA sobre el paquete validado: no reinstala el
    /// cliente y no vuelve a descargar los bytes, así que el fallo anterior no se pierde
    /// como diagnóstico (AC5).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is installed.</param>
    /// <param name="packageId">Opaque identifier of the package that is installed.</param>
    /// <param name="installedVersion">Version of the videogame that is installed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the installation.</returns>
    public Task<InstallationResult> RetryAsync(
        string gameId,
        string packageId,
        string installedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedVersion);

        // El reintento es una operación NUEVA, así que el fallo anterior deja de ser el
        // fallo vigente mientras la operación corre (AC5).
        FailedOperation = null;

        return InstallAsync(gameId, packageId, installedVersion, cancellationToken);
    }

    /// <summary>
    /// Certifies the tree that is ALREADY installed, and tells whether it is a valid
    /// installation of the same version (AC1).
    ///
    /// <para>
    /// La certificación previa decide si hace falta ESCRIBIR algo: una instalación ya
    /// válida de la misma versión NO repite las operaciones del manifiesto (AC1).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is installed.</param>
    /// <param name="manifest">Manifest of the package that is being installed.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The verdict of the tree that is already installed, or <c>null</c> when the tree is
    /// NOT a valid installation of this manifest.
    /// </returns>
    private async Task<InstallationVerdict?> CertifyExistingAsync(
        string gameId,
        InstallationManifest manifest,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var registered = await _stateStore
            .LoadInstallationAsync(gameId, cancellationToken)
            .ConfigureAwait(false);

        if (registered is null || registered.ValidationState != InstallationState.Installed)
        {
            return null;
        }

        if (!string.Equals(registered.InstalledVersion, manifest.PackageVersion, StringComparison.Ordinal))
        {
            return null;
        }

        // La fila declarada dice que la instalación es válida, así que la CONFIRMACIÓN
        // viene del árbol: la operación `verify` del manifiesto corre sobre los archivos
        // que ya están escritos (AC1, AD-7).
        var verified = VerifyExistingTree(manifest, rootPath);

        if (!verified)
        {
            return null;
        }

        return InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: true,
            pathsConfinedToRoot: true,
            repairablePackageAvailable: true);
    }

    /// <summary>
    /// Tells whether the tree that is already installed SATISFIES the <c>verify</c>
    /// operation of the manifest (AC1, AD-7).
    /// </summary>
    /// <param name="manifest">Manifest of the package that is being installed.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <returns><c>true</c> when the tree passes the verification.</returns>
    private bool VerifyExistingTree(InstallationManifest manifest, string rootPath)
    {
        foreach (var operation in manifest.Operations)
        {
            if (operation is not InstallationManifest.Verify verify)
            {
                continue;
            }

            try
            {
                var path = _paths.ResolveInsideRoot(verify.Path);

                if (!File.Exists(path))
                {
                    return false;
                }
            }
            catch (InstallPathOutsideRootException)
            {
                // Una ruta que sale de la raíz NO puede estar en un árbol válido (AD-9).
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Persists the FAILURE of an installation, so the error queda registrado y la
    /// operación es REINTENTABLE sin reinstalar el cliente (AD-8, AD-11, AC5).
    /// </summary>
    /// <param name="gameId">Identifier of the videogame that is installed.</param>
    /// <param name="installedVersion">Version of the videogame that is installed.</param>
    /// <param name="rootPath">Canonical absolute path of the installation root.</param>
    /// <param name="errorCode">Stable code of the error of the catalog.</param>
    /// <param name="reason">Human readable reason of the failure.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>A task that completes when the failure is persisted.</returns>
    private async Task PersistFailureAsync(
        string gameId,
        string installedVersion,
        string rootPath,
        string errorCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var installDirectoryId = InstallationCertification.DeriveInstallDirectoryId(rootPath);

        await _stateStore
            .SaveInstallationFailureAsync(
                new ClientInstallation(
                    gameId,
                    installedVersion,
                    InstallationState.Failed,
                    rootPath,
                    installDirectoryId),
                errorCode,
                cancellationToken)
            .ConfigureAwait(false);

        // AD-15, AD-25: el diagnóstico lleva el `ApiError` COMPLETO del catálogo, que es
        // el sumidero durable del fallo.
        var error = ErrorCatalog.Resolve(errorCode);

        _logger.LogError(
            "La instalación del juego {GameId} queda FALLIDA con el código {Code}: {Message} {Cause} {NextStep} Detalle: {Reason}",
            gameId,
            error.Code,
            error.Message,
            error.Cause,
            error.NextStep,
            reason);
    }

    /// <summary>
    /// Publishes the REAL phase of a FAILED installation, así que el organizador ve que
    /// la operación terminó en fallo y no solo que desapareció (AD-22, FR-10).
    ///
    /// <para>
    /// La publicación usa un token VIVO: un rechazo NO viene de una cancelación, así que
    /// la fase de fallo tiene que llegar al servidor aunque la operación ya no pueda
    /// cancelar nada (AD-22).
    /// </para>
    /// </summary>
    /// <param name="trackedOperation">
    /// Identifier of the TRACKED operation, or <c>null</c> when the client does not track
    /// the installation (AD-22).
    /// </param>
    /// <returns>A task that completes when the phase has been published, or has failed.</returns>
    private Task PublishFailurePhaseAsync(string? trackedOperation) =>
        PublishAsync(InstallPhase.Failed, 0, null, trackedOperation, CancellationToken.None);

    /// <summary>
    /// Publishes the progress of the operation to whoever is listening (AC4).
    /// </summary>
    /// <param name="phase">Phase the operation is in.</param>
    /// <param name="bytesWritten">Bytes that have really been written so far.</param>
    /// <param name="totalBytes">Total bytes that the operation expects to write.</param>
    /// <param name="trackedOperation">
    /// Identifier of the TRACKED operation, or <c>null</c> when the client does not track
    /// the installation (AD-22).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    private async Task PublishAsync(
        InstallPhase phase,
        long bytesWritten,
        long? totalBytes,
        string? trackedOperation,
        CancellationToken cancellationToken)
    {
        ProgressChanged?.Invoke(new InstallProgress(phase, bytesWritten, totalBytes));

        await PublishPhaseAsync(phase, trackedOperation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes the REAL phase of the operation to the server, WITHOUT raising
    /// <see cref="ProgressChanged"/> (AD-22, FR-10).    ///
    /// <para>
    /// La fase REAL se publica al servidor con el identificador de la operación, y el
    /// payload NO lleva porcentaje: un porcentaje sobre un total que no se conoce sería una
    /// mentira (AD-22, FR-10).
    /// </para>
    /// <para>
    /// La publicación de la fase al servidor NO vuelve a raises <see
    /// cref="ProgressChanged"/>: la vista ya recibió esa fase cuando el ejecutor la
    /// reportó, así que un segundo aviso repetiría la barra con la misma fase (AC4).
    /// </para>
    /// </summary>
    /// <param name="phase">Phase the operation is in.</param>
    /// <param name="trackedOperation">
    /// Identifier of the TRACKED operation, or <c>null</c> when the client does not track
    /// the installation (AD-22).
    /// </param>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the phase has been published, or has failed.</returns>
    private async Task PublishPhaseAsync(
        InstallPhase phase,
        string? trackedOperation,
        CancellationToken cancellationToken)
    {
        if (_activeInstallations is null || trackedOperation is null)
        {
            return;
        }

        try
        {
            var identity = await _stateStore
                .LoadIdentityAsync(cancellationToken)
                .ConfigureAwait(false);

            if (identity is null)
            {
                return;
            }

            await _activeInstallations
                .ReportPhaseAsync(identity.CanonicalId, trackedOperation, phase, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación del progreso NUNCA impide que la instalación siga: el
            // servidor que no recibe la fase no puede impedir que el manifiesto se
            // aplique (AD-1, AD-22).
            _logger.LogWarning(
                exception,
                "No se pudo publicar la fase '{Phase}' de la instalación: la instalación sigue.",
                phase);
        }
    }

    /// <summary>
    /// Publishes the progress of the operation to whoever is listening (AC4).
    /// </summary>
    /// <param name="phase">Phase the operation is in.</param>
    /// <param name="bytesWritten">Bytes that have really been written so far.</param>
    /// <param name="totalBytes">Total bytes that the operation expects to write.</param>
    private void Publish(InstallPhase phase, long bytesWritten, long? totalBytes) =>
        ProgressChanged?.Invoke(new InstallProgress(phase, bytesWritten, totalBytes));

    /// <summary>
    /// Starts the TRACKING of the installation operation, MINTANDO su identificador
    /// (AD-22).
    ///
    /// <para>
    /// Sin tracker la operación NO se declara al servidor, y la instalación NO se rompe:
    /// la proyección es un complemento y no su motivo de vida (AD-1, AD-22).
    /// </para>
    /// <para>
    /// La operación que empieza se PUBLICA al servidor con el estado COMPLETO del
    /// cliente, así que la tabla del organizador la ve EN CURSO desde el principio y no
    /// solo cuando el cliente publique su estado por otra vía (AD-22, FR-10).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the game that is installed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The identifier of the tracked operation, or <c>null</c> when the client does not
    /// track the installation.
    /// </returns>
    private async Task<string?> BeginTrackedOperationAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        if (_activeInstallations is null)
        {
            return null;
        }

        string? operationId = null;

        try
        {
            var identity = await _stateStore
                .LoadIdentityAsync(cancellationToken)
                .ConfigureAwait(false);

            if (identity is null)
            {
                return null;
            }

            operationId = _activeInstallations.Begin(identity.CanonicalId, gameId);

            // AD-22, FR-10: la operación EN CURSO se declara al servidor antes de que la
            // instalación avance, así que el organizador la ve desde el principio.
            await PublishTrackedStateAsync(cancellationToken).ConfigureAwait(false);

            return operationId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // AD-22: la operación YA está en el tracker cuando la publicación se
            // cancela, así que la SUELTA aquí: si no, quedaría EN CURSO para siempre
            // sin que ninguna puerta la cierre, porque esta excepción sale ANTES del
            // <c>try/finally</c> de <see cref="InstallAsync"/> (AD-22, FR-10).
            AbandonTrackedOperation(operationId, gameId);

            throw;
        }
        catch (Exception exception)
        {
            // AD-22: la operación ya declarada que NO se pudo publicar tampoco puede
            // quedar en curso: se suelta para que no sobreviva a una declaración que
            // falló (AD-22, FR-10).
            AbandonTrackedOperation(operationId, gameId);

            // El tracking NO impide que la instalación siga (AD-1, AD-22).
            _logger.LogWarning(
                exception,
                "No se pudo iniciar el seguimiento de la instalación del juego {GameId}: la instalación sigue.",
                gameId);

            return null;
        }
    }

    /// <summary>
    /// Finishes the TRACKING of the installation operation: la operación deja de estar en
    /// curso, y su instalación PERSISTE (AD-22, AD-26).
    ///
    /// <para>
    /// La operación que termina se PUBLICA al servidor con el estado COMPLETO del
    /// cliente, así que la tabla del organizador la ve TERMINADA y no la muestra como
    /// activa (AD-22, FR-10).
    /// </para>
    /// </summary>
    /// <param name="trackedOperation">Identifier of the tracked operation, or <c>null</c>.</param>
    /// <returns>A task that completes when the operation has been finished.</returns>
    private async Task FinishTrackedOperationAsync(string? trackedOperation)
    {
        if (_activeInstallations is null || trackedOperation is null)
        {
            return;
        }

        _activeInstallations.Complete(trackedOperation);

        // AD-22, FR-10: la operación deja de estar EN CURSO, así que el servidor tiene que
        // recibir el estado COMPLETO del cliente para quitarla de la lista de operaciones
        // en curso.
        //
        // AD-22: la operación que terminó puede ser una CANCELADA, así que el token de
        // la instalación ya está CANCELADO y su publicación no llegaría nunca al
        // servidor: la operación cancelada seguiría EN CURSO en la tabla del
        // organizador. El CIERRE se publica con un token VIVO, así que la vista se
        // limpia aunque la operación se cancele (AD-22, FR-10).
        //
        // El cierre es de MEJOR ESFUERZO: una publicación que falla NO deja la
        // operación en el tracker, que ya la soltó (AD-1, AD-22).
        using var closure = new CancellationTokenSource();

        await PublishTrackedStateAsync(closure.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// ABANDONS the TRACKING of an installation operation that was declared but could not
    /// be published, así que la operación NO queda en curso para siempre (AD-22, FR-10).
    /// </summary>
    /// <param name="trackedOperation">
    /// Identifier of the tracked operation, or <c>null</c> when the operation was never
    /// declared.
    /// </param>
    /// <param name="gameId">Identifier of the game that is being installed.</param>
    private void AbandonTrackedOperation(string? trackedOperation, string gameId)
    {
        if (_activeInstallations is null || trackedOperation is null)
        {
            return;
        }

        _activeInstallations.Complete(trackedOperation);

        _logger.LogWarning(
            "La instalación del juego {GameId} no pudo declararse en curso, así que la operación {OperationId} se suelta: el servidor no la mostrará como activa.",
            gameId,
            trackedOperation);
    }

    /// <summary>
    /// Publishes the COMPLETE state of the client, de MEJOR ESFUERZO (AD-22, FR-10).
    ///
    /// <para>
    /// La publicación NUNCA impide que la instalación siga: un servidor que no recibe el
    /// estado no puede impedir que el manifiesto se aplique (AD-1, AD-22). La
    /// cancelación, en cambio, SÍ se propaga: una instalación cancelada no tiene que
    /// publicar nada más (AD-1).
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the publication.</param>
    /// <returns>A task that completes when the publication has been attempted.</returns>
    private async Task PublishTrackedStateAsync(CancellationToken cancellationToken)
    {
        if (_activeInstallations is null)
        {
            return;
        }

        try
        {
            await _activeInstallations
                .PublishStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // La publicación del estado NUNCA impide que la instalación siga (AD-1, AD-22).
            _logger.LogWarning(
                exception,
                "No se pudo publicar el estado del cliente tras el cambio de la instalación en curso: la instalación sigue.");
        }
    }

    /// <summary>
    /// Sumidero del progreso que PUBLICA en el coordinador, así que la vista recibe la
    /// fase y el porcentaje de los bytes REALES (AC4).
    ///
    /// <para>
    /// El sumidero publica ALSO la fase REAL al servidor, con el identificador de la
    /// operación que el tracker minte (AD-22, FR-10).
    /// </para>
    /// </summary>
    private sealed class CoordinatorProgressSink : IInstallProgressSink
    {
        private readonly InstallationCoordinator _coordinator;
        private readonly string? _trackedOperation;
        private readonly CancellationToken _cancellationToken;
        private readonly Lock _gate = new();

        private readonly Queue<ReportedPhase> _pending = new();

        public CoordinatorProgressSink(
            InstallationCoordinator coordinator,
            string? trackedOperation = null,
            CancellationToken cancellationToken = default)
        {
            _coordinator = coordinator;
            _trackedOperation = trackedOperation;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Bytes that were really written in the last publication.</summary>
        public long LastBytesWritten { get; private set; }

        /// <summary>Total bytes in the last publication.</summary>
        public long? LastTotalBytes { get; private set; }

        public void Report(InstallPhase phase, long bytesWritten, long? totalBytes)
        {
            LastBytesWritten = bytesWritten;
            LastTotalBytes = totalBytes;

            // AC4: la vista recibe el progreso UNA sola vez por publicación, así que la
            // barra no se repite con la misma fase.
            _coordinator.Publish(phase, bytesWritten, totalBytes);

            // AD-22: la fase REAL viaja al servidor con el identificador de la
            // operación, y SIN porcentaje (AD-22, FR-10). La publicación se ENCOLA y se
            // ESPERA en <see cref="FlushAsync"/>: un «dispara y olvida» publicaría la
            // fase sin ORDEN y dejaría la cancelación sin observar.
            //
            // AD-22: las fases se ENCOLAN en el ORDEN en que el ejecutor las declaró,
            // así que ninguna fase intermedia se pierde: la última fase no sustituye a
            // las anteriores, y la espera privilegiada llega al servidor en su momento
            // (AD-22, FR-10).
            lock (_gate)
            {
                _pending.Enqueue(new ReportedPhase(phase, bytesWritten, totalBytes));
            }
        }

        /// <summary>
        /// Publishes to the server EVERY phase that the executor reported, in the ORDER in
        /// which it was reported (AD-22, FR-10).
        ///
        /// <para>
        /// La publicación se ESPERA: una cancelación se observa y una excepción se
        /// propaga, así que la operación cancelada no deja una tarea sin observar
        /// (AD-1).
        /// </para>
        /// <para>
        /// La publicación al servidor NO vuelve a raises <see
        /// cref="InstallationCoordinator.ProgressChanged"/>: la vista ya recibió cada
        /// fase cuando el ejecutor la reportó, así que un segundo aviso repetiría la
        /// barra con la misma fase (AC4, B04).
        /// </para>
        /// </summary>
        /// <returns>A task that completes when the phases have been published.</returns>
        public async Task FlushAsync()
        {
            while (true)
            {
                ReportedPhase phase;

                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        return;
                    }

                    phase = _pending.Dequeue();
                }

                await _coordinator
                    .PublishPhaseAsync(phase.Phase, _trackedOperation, _cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A phase that the executor reported, que espera a ser publicada (AD-22).
        /// </summary>
        /// <param name="Phase">Phase the operation was in.</param>
        /// <param name="BytesWritten">Bytes that had really been written so far.</param>
        /// <param name="TotalBytes">Total bytes that the operation expects to write.</param>
        private readonly record struct ReportedPhase(
            InstallPhase Phase,
            long BytesWritten,
            long? TotalBytes);
    }

    /// <summary>
    /// Verifies the signature and the schema version of the signed manifest (AD-2, AD-8).
    ///
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <param name="envelope">Signed envelope that the server served.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the verification.</returns>
    private async Task<ManifestVerification> VerifySignedManifestAsync(
        string packageId,
        InstallationManifest.SignedEnvelope envelope,
        CancellationToken cancellationToken)
    {
        // AD-27: la validación se hace contra el ancla YA FIJADA. Sin ancla fijada no hay
        // contra qué validar, así que la operación no se puede lanzar y no se escribe nada.
        var anchor = await _anchorReader
            .LoadTrustAnchorAsync(cancellationToken)
            .ConfigureAwait(false);

        if (anchor is null)
        {
            return ManifestVerification.Failed(
                ErrorCatalog.AnchorFingerprintMismatchCode,
                "El cliente no tiene un ancla de confianza fijada: no puede validar el manifiesto y no escribe nada en disco.");
        }

        byte[] publicKey;

        try
        {
            publicKey = Convert.FromBase64String(anchor.PublicKey);
        }
        catch (FormatException)
        {
            return ManifestVerification.Failed(
                ErrorCatalog.ManifestSignatureInvalidCode,
                "La clave pública del ancla de confianza fijada no es válida: el cliente no escribe nada en disco.");
        }

        // AD-2, AD-8: los bytes que se verifican son EXACTAMENTE los que el servidor
        // firmó: el texto canónico viaja tal cual y no se recompone aquí.
        if (!_verifier.Verify(envelope.Manifest, envelope.Signature, publicKey))
        {
            return ManifestVerification.Failed(
                ErrorCatalog.ManifestSignatureInvalidCode,
                $"La firma del manifiesto del paquete '{packageId}' no valida contra el ancla de confianza fijada: el cliente no escribe nada en disco.");
        }

        InstallationManifest? manifest;
        int schemaVersion;

        try
        {
            manifest = System.Text.Json.JsonSerializer.Deserialize<InstallationManifest>(
                envelope.Manifest,
                ManifestJsonOptions);

            schemaVersion = manifest?.SchemaVersion ?? 0;
        }
        catch (System.Text.Json.JsonException exception)
        {
            // AD-7, AC2: el deserializador POLIMÓRFICO del contrato es el que aplica el
            // conjunto cerrado de operaciones. Un discriminador que el contrato no
            // reconoce es una operación FUERA del conjunto, y su rechazo lleva la causa
            // propia en lugar de la de un manifiesto malformado (AD-15, AD-25).
            if (IsUnrecognizedDiscriminator(exception))
            {
                return ManifestVerification.Failed(
                    ErrorCatalog.ManifestOperationNotAllowedCode,
                    $"El manifiesto firmado del paquete '{packageId}' declara una operación fuera del conjunto cerrado de las operaciones permitidas: el cliente no escribe nada en disco.");
            }

            return ManifestVerification.Failed(
                ErrorCatalog.ManifestSignatureInvalidCode,
                $"El manifiesto firmado del paquete '{packageId}' no se puede leer: el cliente no escribe nada en disco.");
        }
        catch (NotSupportedException)
        {
            // AD-7: un discriminador que el contrato no reconoce es una operación fuera
            // del conjunto cerrado, y el rechazo lleva su propia causa (AC2).
            return ManifestVerification.Failed(
                ErrorCatalog.ManifestOperationNotAllowedCode,
                $"El manifiesto firmado del paquete '{packageId}' declara una operación fuera del conjunto cerrado de las operaciones permitidas: el cliente no escribe nada en disco.");
        }

        if (manifest is null)
        {
            return ManifestVerification.Failed(
                ErrorCatalog.ManifestSignatureInvalidCode,
                $"El manifiesto firmado del paquete '{packageId}' no se puede leer: el cliente no escribe nada en disco.");
        }

        // AD-8: la versión del esquema que declara el manifiesto tiene que ser una que el
        // cliente entienda.
        if (schemaVersion != SupportedManifestSchemaVersion)
        {
            return ManifestVerification.Failed(
                ErrorCatalog.SchemaVersionNotSupportedCode,
                $"El manifiesto del paquete '{packageId}' declara la versión de esquema {schemaVersion}, que este cliente no soporta: el cliente no escribe nada en disco.");
        }

        return ManifestVerification.Succeeded(manifest);
    }

    /// <summary>
    /// Validates the INTEGRITY of the bytes of the package against the hash that the
    /// SIGNED manifest declares (AD-2, AD-8).
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <param name="manifest">Manifest that was signed.</param>
    /// <param name="packagePath">Absolute path of the file that holds the bytes.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The result of the validation.</returns>
    private async Task<IntegrityOutcome> ValidateIntegrityAsync(
        string packageId,
        InstallationManifest manifest,
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(packagePath))
        {
            return IntegrityOutcome.Failed(
                ErrorCatalog.PackageIntegrityMismatchCode,
                $"No hay bytes del paquete '{packageId}' para verificar su integridad.");
        }

        string actual;

        await using (var content = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true))
        {
            actual = await LANPartyHub.Client.Domain.Integrity.PackageIntegrity
                .ComputeSha256Async(content, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!LANPartyHub.Client.Domain.Integrity.PackageIntegrity.Matches(manifest.Sha256, actual))
        {
            return IntegrityOutcome.Failed(
                ErrorCatalog.PackageIntegrityMismatchCode,
                $"El sha256 calculado del paquete '{packageId}' no coincide con el del manifiesto firmado.");
        }

        return IntegrityOutcome.Succeeded();
    }

    /// <summary>
    /// Deletes the partial file of a package (AD-8, AC1).
    ///
    /// <param name="packagePath">Absolute path of the partial file.</param>
    private void DiscardPartial(string packagePath)
    {
        try
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "No se pudo borrar el archivo parcial del paquete: el hash no coincidía.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "No se pudo borrar el archivo parcial del paquete: el hash no coincidía.");
        }
    }

    /// <summary>
    /// Tells whether a failure of the reading of the manifiesto is a discriminador que
    /// el contrato no reconoce (AD-7).
    ///
    /// <para>
    /// El mensaje del deserializador polimórfico nombra el discriminador que no
    /// pertenece al conjunto cerrado, y esa es la diferencia entre un manifiesto con una
    /// operación prohibida y un manifiesto que no se puede leer (AD-7, AD-15, AD-25).
    /// </para>
    /// </summary>
    /// <param name="exception">Failure of the reading of the manifest.</param>
    /// <returns><c>true</c> when the manifest declares an operation outside the closed set.</returns>
    private static bool IsUnrecognizedDiscriminator(System.Text.Json.JsonException exception) =>
        exception.Message.Contains(
            "unrecognized type discriminator",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the rejection of an operation, with its cause (AC2, NFR-5).
    ///
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <returns>The result of the rejection.</returns>
    private InstallationResult Reject(string code, string reason, string packageId)
    {
        var error = ErrorCatalog.Resolve(code);

        _logger.LogWarning(
            "La instalación del paquete {PackageId} se rechaza con el código {Code}: {Reason}",
            packageId,
            code,
            reason);

        return InstallationResult.Rejected(error, reason);
    }

    /// <summary>
    /// Version of the schema of the installation manifest that this client supports
    /// (AD-7, AD-8).
    /// </summary>
    public const int SupportedManifestSchemaVersion = 1;

    /// <summary>
    /// Options of the reading of the canonical manifest. The property names of the
    /// canonical text are in camel case, and the deserializer does not distinguish
    /// mayúsculas de minúsculas en el nombre de las propiedades (AD-2).
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions ManifestJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>
    /// Result of the verification of the signature and the schema version of a signed
    /// manifest (AD-8).
    /// </summary>
    private sealed record ManifestVerification
    {
        public InstallationManifest? Manifest { get; init; }

        public ApiError? Error { get; init; }

        public string? Reason { get; init; }

        public static ManifestVerification Succeeded(InstallationManifest manifest) =>
            new() { Manifest = manifest };

        public static ManifestVerification Failed(string code, string reason) =>
            new() { Error = ErrorCatalog.Resolve(code), Reason = reason };
    }

    /// <summary>
    /// Result of the validation of the integrity of the bytes of a package (AD-8).
    /// </summary>
    private sealed record IntegrityOutcome
    {
        public bool IsValid { get; init; }

        public ApiError? Error { get; init; }

        public string? Reason { get; init; }

        public static IntegrityOutcome Succeeded() => new() { IsValid = true };

        public static IntegrityOutcome Failed(string code, string reason) =>
            new() { IsValid = false, Error = ErrorCatalog.Resolve(code), Reason = reason };
    }
}
