using System.Text.Json;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Client.Domain.StateMachines;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// The case of use of the REPAIR of an invalid installation, reusing the package that was
/// already VALIDATED (AC1, AC2, UX-DR-17, UX-DR-18, UX-DR-20, AD-9, AD-10, AD-22).
///
/// <para>
/// La reparación es UNA SOLA VÍA: reutiliza el EJECUTOR y la CERTIFICACIÓN de 7.1/7.3, así
/// que no existe una segunda definición de «instalación válida» ni de reparabilidad
/// (AC1, AC5, AC7).
/// </para>
/// <para>
/// La reparación es de CERO RED: NO llama a <c>IDownloadIntentClient</c>, ni a
/// <c>ITransferEngine</c>, ni a <c>ISignedManifestClient</c>. El paquete validado y el
/// manifiesto FIRMADO en caché son LOCALES, así que la descarga NO se repite
/// (AC1, UX-DR-20).
/// </para>
/// <para>
/// El ORDEN es NORMATIVO: (1) localizar el paquete ya validado; (2) re-verificar su
/// <c>sha256</c> contra el que declara el manifiesto FIRMADO en caché; (3) SÓLO ENTONCES
/// reaplicar el manifiesto dentro de la raíz; (4) CERTIFICAR el árbol; (5) PERSISTIR el
/// estado. Un <c>sha256</c> que ya no coincide deja de ser reparable y el motivo visible
/// es el de la re-descarga (AC1, AC2).
/// </para>
/// <para>
/// El estado terminal <c>Repairable</c> se REABRE: la máquina pasa por
/// <c>Repairable → Installing → ValidatingInstallation → Installed|Failed</c> (AD-10,
/// AC1).
/// </para>
/// </summary>
public sealed class InstallationRepairService
{
    private readonly IValidatedPackageStore _packages;
    private readonly IManifestCache _manifestCache;
    private readonly ICatalogSnapshotStore _snapshots;
    private readonly IInstallPathResolver _paths;
    private readonly IInstallTreeWriter _tree;
    private readonly IClientStateStore _stateStore;
    private readonly ILogger<InstallationRepairService> _logger;

    /// <summary>Raised when the progress of the repair changes (AC4).</summary>
    public event Action<InstallProgress>? ProgressChanged;

    /// <summary>Creates the case of use of the repair of an installation.</summary>
    /// <param name="packages">Port of the localization of the package already validated.</param>
    /// <param name="manifestCache">Port of the cache of the signed manifests.</param>
    /// <param name="snapshots">Port of the persistence of the snapshot of the catalog.</param>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <param name="tree">Port of the application of the typed operations of the manifest.</param>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="logger">Log of the case of use.</param>
    public InstallationRepairService(
        IValidatedPackageStore packages,
        IManifestCache manifestCache,
        ICatalogSnapshotStore snapshots,
        IInstallPathResolver paths,
        IInstallTreeWriter tree,
        IClientStateStore stateStore,
        ILogger<InstallationRepairService> logger)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(manifestCache);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(logger);

        _packages = packages;
        _manifestCache = manifestCache;
        _snapshots = snapshots;
        _paths = paths;
        _tree = tree;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <summary>
    /// Repairs an invalid installation from the package that was already VALIDATED,
    /// WITHOUT downloading it again (AC1, UX-DR-20).
    ///
    /// <para>
    /// La reparación NUNCA descarga: si el paquete validado no está disponible o su hash
    /// diverge, la operación se RECHAZA con el motivo de la re-descarga y no se escribe
    /// nada (AC1, AC2).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame whose installation is repaired.</param>
    /// <param name="installedVersion">Version of the videogame that is installed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The outcome of the repair.</returns>
    public async Task<RepairOutcome> RepairAsync(
        string gameId,
        string installedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedVersion);

        // PASO 0: el paquete que se repara es el de la VERSIÓN INSTALADA, porque es el
        // único que describe el árbol que hay en disco (AC1).
        var packageId = await ResolvePackageIdAsync(gameId, installedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (packageId is null)
        {
            _logger.LogWarning(
                "El cliente no conoce el paquete de la versión {Version} del juego {GameId}: la instalación NO se puede reparar sin volver a descargar.",
                installedVersion,
                gameId);

            return Reject(
                ErrorCatalog.InstallationPackageMismatchCode,
                $"El cliente no conoce el paquete de la versión '{installedVersion}' del juego '{gameId}': la instalación no se puede reparar sin volver a descargar.");
        }

        // PASO 1: el paquete ya validado tiene que estar EN DISCO. Sin él no hay
        // reparación, y el motivo visible es el de la RE-DESCARGA (AC1, AC2).
        var packagePath = _packages.LocateValidated(packageId);

        if (packagePath is null)
        {
            _logger.LogWarning(
                "El cliente no tiene el paquete validado {PackageId} en disco: la instalación del juego {GameId} NO se puede reparar sin volver a descargar.",
                packageId,
                gameId);

            return Reject(
                ErrorCatalog.InstallationPackageMismatchCode,
                $"El paquete ya validado '{packageId}' no está disponible en el equipo: la instalación no se puede reparar sin volver a descargar.");
        }

        // PASO 2: el manifiesto FIRMADO sale de la CACHÉ local, así que la reparación no
        // habla con el servidor (AC1, UX-DR-20).
        var canonical = await _manifestCache
            .LoadAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(canonical))
        {
            _logger.LogWarning(
                "El cliente no tiene el manifiesto firmado del paquete {PackageId} en caché: la instalación del juego {GameId} NO se puede reparar sin volver a descargar.",
                packageId,
                gameId);

            return Reject(
                ErrorCatalog.InstallationPackageMismatchCode,
                $"El manifiesto firmado del paquete '{packageId}' no está en la caché del cliente: la instalación no se puede reparar sin volver a descargar.");
        }

        if (!TryReadManifest(canonical, out var manifest))
        {
            _logger.LogWarning(
                "El manifiesto firmado del paquete {PackageId} en caché no se puede leer: la instalación del juego {GameId} NO se puede reparar.",
                packageId,
                gameId);

            return Reject(
                ErrorCatalog.ManifestSignatureInvalidCode,
                $"El manifiesto firmado del paquete '{packageId}' no se puede leer: la instalación no se puede reparar.");
        }

        // PASO 3: el `sha256` se RE-VERIFICA ANTES de extraer. Si ya no coincide, el
        // paquete deja de ser reparable y el motivo visible es el de la re-descarga
        // (AC1, AC2, AD-2, AD-8).
        if (!await MatchesSignedHashAsync(manifest!, packagePath, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "El sha256 del paquete validado {PackageId} ya NO coincide con el del manifiesto firmado: la instalación del juego {GameId} deja de ser reparable.",
                packageId,
                gameId);

            return Reject(
                ErrorCatalog.PackageIntegrityMismatchCode,
                $"El paquete ya validado '{packageId}' no conserva su hash: la instalación no se puede reparar sin volver a descargar.");
        }

        // La máquina REABRE el estado terminal `Repairable` y vuelve a pasar por
        // `Installing` y por `ValidatingInstallation` (AD-10, AC1).
        var machine = new InstallationStateMachine(InstallationState.Repairable);

        machine.TryTransitionTo(InstallationState.Installing);

        var rootPath = _paths.RootPath;

        Directory.CreateDirectory(rootPath);

        var sink = new RepairProgressSink(this);

        // PASO 4: se reaplica el MANIFIESTO dentro de la raíz configurada, con el mismo
        // ejecutor y el mismo confine que usa la instalación (AD-7, AD-9).
        InstallTreeOutcome tree;

        try
        {
            tree = await _tree
                .ApplyAsync(manifest!, rootPath, packagePath, sink, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PrivilegedOperationDeclinedException exception)
        {
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

            Publish(InstallPhase.Failed, sink.LastBytesWritten, sink.LastTotalBytes);

            return Reject(
                ErrorCatalog.InstallPrivilegedConfirmationDeclinedCode,
                exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            machine.TryTransitionTo(InstallationState.Failed);

            await PersistFailureAsync(
                gameId,
                installedVersion,
                rootPath,
                ErrorCatalog.InstallOperationFailedCode,
                exception.Message,
                cancellationToken).ConfigureAwait(false);

            Publish(InstallPhase.Failed, sink.LastBytesWritten, sink.LastTotalBytes);

            _logger.LogError(
                exception,
                "La reparación de la instalación del juego {GameId} falló al aplicar el manifiesto: la operación queda fallida.",
                gameId);

            return Reject(
                ErrorCatalog.InstallOperationFailedCode,
                $"La reparación de la instalación del juego '{gameId}' falló al aplicar el manifiesto: {exception.Message}");
        }

        if (tree.RejectedPath is not null)
        {
            _logger.LogWarning(
                "La reparación de la instalación del juego {GameId} escribió una ruta que resuelve en '{RejectedPath}', fuera de la raíz de instalación '{RootPath}'.",
                gameId,
                tree.RejectedPath,
                rootPath);
        }

        // PASO 5: la CERTIFICACIÓN es la ÚNICA definición de instalación válida, y las TRES
        // condiciones se evaluan JUNTAS (AC5, AC1).
        //
        // La primera condición se afirma porque el `sha256` del paquete coincidió con el
        // del manifiesto FIRMADO en el paso 3, así que el paquete sigue validado contra el
        // ancla fijada (AD-2, AD-8).
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: tree.VerifyPassed,
            pathsConfinedToRoot: tree.PathsConfinedToRoot,
            repairablePackageAvailable: true);

        machine.TryTransitionTo(InstallationState.ValidatingInstallation);
        machine.TryTransitionTo(verdict.State);

        // El identificador de la raíz es ESTABLE: se deriva de su ruta canónica, así que NO
        // cambia ante la reparación (AC1, AD-22).
        var installDirectoryId = InstallationCertification.DeriveInstallDirectoryId(rootPath);

        // PASO 6: el estado se PERSISTE. `CLIENT_INSTALLATION` mueve `validationState` y
        // conserva `installDirectoryId` (AD-11, AD-22, AC1).
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

        Publish(
            verdict.IsInstalled ? InstallPhase.Completed : InstallPhase.Failed,
            sink.LastBytesWritten,
            sink.LastTotalBytes);

        if (verdict.IsInstalled)
        {
            _logger.LogInformation(
                "La instalación del juego {GameId} se reparó desde el paquete ya validado y quedó CERTIFICADA con el identificador de raíz {InstallDirectoryId}.",
                gameId,
                installDirectoryId);
        }
        else
        {
            _logger.LogWarning(
                "La reparación de la instalación del juego {GameId} NO deja una instalación válida: {Reason}",
                gameId,
                verdict.ReasonText);
        }

        return RepairOutcome.Repaired(verdict, installDirectoryId, rootPath);
    }

    /// <summary>
    /// Resolves the identifier of the package that describes the INSTALLED tree (AC1,
    /// AD-9).
    ///
    /// <para>
    /// El catálogo admite VARIOS paquetes vivos del mismo juego, así que el paquete que
    /// se busca es el de la VERSIÓN INSTALADA, porque es el único que describe el árbol
    /// que hay en disco: reparar con el manifiesto de otra versión dejaría el juego
    /// instalado y válido sin poder jugarse (AC1).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="installedVersion">Version of the videogame that is installed.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The identifier of the package, or <c>null</c> when the client knows none.</returns>
    private async Task<string?> ResolvePackageIdAsync(
        string gameId,
        string installedVersion,
        CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            return null;
        }

        var package = snapshot.Packages.FirstOrDefault(candidate =>
            string.Equals(candidate.GameId, gameId, StringComparison.Ordinal)
            && string.Equals(candidate.Version, installedVersion, StringComparison.Ordinal));

        return package?.PackageId;
    }

    /// <summary>
    /// Re-verifies the <c>sha256</c> of the validated package against the one that the
    /// SIGNED manifest declares (AD-2, AD-8, AC1).
    /// </summary>
    /// <param name="manifest">Signed manifest of the package.</param>
    /// <param name="packagePath">Absolute path of the file that holds the bytes.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns><c>true</c> when the hash still matches.</returns>
    private async Task<bool> MatchesSignedHashAsync(
        InstallationManifest manifest,
        string packagePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(packagePath))
        {
            return false;
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

        return LANPartyHub.Client.Domain.Integrity.PackageIntegrity.Matches(
            manifest.Sha256,
            actual);
    }

    /// <summary>
    /// Reads the manifest that the signed canonical text carries (AD-2, AD-8).
    /// </summary>
    /// <param name="canonicalManifest">Canonical JSON text of the manifest.</param>
    /// <param name="manifest">The manifest that was read.</param>
    /// <returns><c>true</c> when the manifest could be read.</returns>
    private static bool TryReadManifest(
        string canonicalManifest,
        out InstallationManifest? manifest)
    {
        try
        {
            manifest = JsonSerializer.Deserialize<InstallationManifest>(
                canonicalManifest,
                ManifestJsonOptions);

            return manifest is not null;
        }
        catch (JsonException)
        {
            manifest = null;

            return false;
        }
        catch (NotSupportedException)
        {
            manifest = null;

            return false;
        }
    }

    /// <summary>
    /// Persists the FAILURE of the repair, so the error queda registrado (AD-11, AC5).
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

        var error = ErrorCatalog.Resolve(errorCode);

        _logger.LogError(
            "La reparación de la instalación del juego {GameId} queda FALLIDA con el código {Code}: {Message} {Cause} {NextStep} Detalle: {Reason}",
            gameId,
            error.Code,
            error.Message,
            error.Cause,
            error.NextStep,
            reason);
    }

    /// <summary>
    /// Publishes the progress of the repair to whoever is listening (AC4).
    /// </summary>
    /// <param name="phase">Phase the operation is in.</param>
    /// <param name="bytesWritten">Bytes that have really been written so far.</param>
    /// <param name="totalBytes">Total bytes that the operation expects to write.</param>
    private void Publish(InstallPhase phase, long bytesWritten, long? totalBytes) =>
        ProgressChanged?.Invoke(new InstallProgress(phase, bytesWritten, totalBytes));

    /// <summary>
    /// Builds the rejection of the repair, with its cause (AC1, AD-15).
    /// </summary>
    /// <param name="code">Stable code of the error of the catalog.</param>
    /// <param name="reason">Human readable reason of the rejection.</param>
    /// <returns>The outcome of the rejection.</returns>
    private RepairOutcome Reject(string code, string reason)
    {
        var error = ErrorCatalog.Resolve(code);

        _logger.LogWarning(
            "La reparación se rechaza con el código {Code}: {Reason}",
            code,
            reason);

        return RepairOutcome.Rejected(error, reason);
    }

    /// <summary>
    /// Options of the reading of the canonical manifest (AD-2).
    /// </summary>
    private static readonly JsonSerializerOptions ManifestJsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sumidero del progreso que PUBLICA en el caso de uso, así que la vista recibe la
    /// fase y el porcentaje de los bytes REALES (AC4).
    /// </summary>
    private sealed class RepairProgressSink : IInstallProgressSink
    {
        private readonly InstallationRepairService _service;

        public RepairProgressSink(InstallationRepairService service) => _service = service;

        /// <summary>Bytes that were really written in the last publication.</summary>
        public long LastBytesWritten { get; private set; }

        /// <summary>Total bytes in the last publication.</summary>
        public long? LastTotalBytes { get; private set; }

        public void Report(InstallPhase phase, long bytesWritten, long? totalBytes)
        {
            LastBytesWritten = bytesWritten;
            LastTotalBytes = totalBytes;

            _service.Publish(phase, bytesWritten, totalBytes);
        }
    }
}
