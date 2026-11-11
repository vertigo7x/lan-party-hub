using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Application.Install;

/// <summary>
/// Executor of the TYPED operations of the installation manifest (AD-7, AD-9, AC1, AC2,
/// AC4).
///
/// <para>
/// El ejecutor aplica las SEIS operaciones del conjunto cerrado, en el orden que el
/// manifiesto declara, y cada escritura pasa por el RESOLVEDOR de rutas: la ruta
/// canónica se comprueba antes de cada escritura, no sólo al principio (AD-9).
/// </para>
/// <para>
/// La operación PRIVILEGIADA pide CONFIRMACIÓN EXPLÍCITA antes de ejecutarse, y la
/// pregunta la hace el puerto de la confirmación: el ejecutor NO sabe cómo se pregunta,
/// sólo cuándo (AD-1, AC-2).
/// </para>
/// <para>
/// El ejecutor publica el PROGRESO con la fase y el porcentaje entero de los bytes
/// reales escritos, así que la barra nunca dibuja una fracción que el texto no declare
/// (AC4, UX-DR-13).
/// </para>
/// </summary>
public sealed class InstallOperationExecutor
{
    private readonly IInstallPathResolver _paths;
    private readonly IInstallArchiveExtractor _extractor;
    private readonly IPrivilegedOperationConfirmation _confirmation;
    private readonly ILogger<InstallOperationExecutor> _logger;

    /// <summary>Creates the executor of the typed operations of the manifest.</summary>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <param name="extractor">Port of the extraction of the archives of the package.</param>
    /// <param name="confirmation">Port of the confirmation of the privileged operations.</param>
    /// <param name="logger">Log of the executor.</param>
    public InstallOperationExecutor(
        IInstallPathResolver paths,
        IInstallArchiveExtractor extractor,
        IPrivilegedOperationConfirmation confirmation,
        ILogger<InstallOperationExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _extractor = extractor;
        _confirmation = confirmation;
        _logger = logger;
    }

    /// <summary>
    /// Applies the typed operations of the manifest inside the installation root (AD-7,
    /// AD-9, AC1, AC2).
    ///
    /// <para>
    /// La raíz que confine las escrituras es la que recibe la llamada, y NO la raíz
    /// configurada: durante una ACTUALIZACIÓN el árbol nuevo se prepara en una raíz de
    /// PREPARACIÓN y la anterior sigue intacta hasta que la nueva se certify (AD-9, AC4,
    /// FR-11).
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
    public async Task<InstallTreeOutcome> ApplyAsync(
        InstallationManifest manifest,
        string rootPath,
        string? packagePath,
        IInstallProgressSink progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var verifyPassed = true;
        var pathsConfinedToRoot = true;
        var operationNotAllowed = false;
        var declined = false;
        string? declinedOperation = null;
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

        var totalBytes = EstimateTotalBytes(manifest, packagePath);
        var writtenBytes = 0L;

        foreach (var operation in manifest.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // AD-7, AC2: la operación PRIVILEGIADA pide confirmación ANTES de ejecutarse.
            // La regla la dice el dominio y la interfaz NO la rederiva (AD-1).
            if (PrivilegedOperationPolicy.IsPrivileged(operation))
            {
                var name = PrivilegedOperationPolicy.NameOf(operation) ?? "desconocida";

                progress.Report(InstallPhase.AwaitingConfirmation, writtenBytes, totalBytes);

                var confirmed = await _confirmation
                    .ConfirmAsync(name, Describe(operation), cancellationToken)
                    .ConfigureAwait(false);

                if (!confirmed)
                {
                    // El decline NO es una escritura: la operación no se ejecuta y la
                    // instalación queda FALLIDA con su error registrado (AC2, AC5).
                    declined = true;
                    declinedOperation = name;

                    _logger.LogWarning(
                        "El jugador canceló la confirmación de la operación privilegiada '{Operation}' de la instalación: la operación no se ejecuta.",
                        name);

                    break;
                }
                progress.Report(InstallPhase.ApplyingManifest, writtenBytes, totalBytes);
            }
            else
            {
                progress.Report(PhaseOf(operation), writtenBytes, totalBytes);
            }

            // AD-9: la comprobación del confine se REPITE antes de cada escritura, así
            // que una ruta que sale de la raíz se rechaza en el momento de escribirla.
            switch (operation)
            {
                case InstallationManifest.Extract extract:
                {
                    if (!TryResolve(rootPath, extract.DestinationPath, out _, out var extractRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= extractRejection;

                        break;
                    }

                    if (packagePath is null)
                    {
                        throw new InvalidOperationException(
                            "La operación de extracción del manifiesto necesita los bytes del paquete, y no están disponibles en disco.");
                    }

                    progress.Report(InstallPhase.Extracting, writtenBytes, totalBytes);

                    var written = await _extractor
                        .ExtractAsync(rootPath, packagePath, extract.ArchivePath, extract.DestinationPath, destinationIsDirectory: true, cancellationToken)
                        .ConfigureAwait(false);

                    writtenBytes += written;

                    progress.Report(InstallPhase.Extracting, writtenBytes, totalBytes);

                    break;
                }

                case InstallationManifest.CreateDirectory createDirectory:
                {
                    if (!TryResolve(rootPath, createDirectory.Path, out var directoryPath, out var directoryRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= directoryRejection;

                        break;
                    }

                    Directory.CreateDirectory(directoryPath);

                    break;
                }

                case InstallationManifest.CopyFile copyFile:
                {
                    if (!TryResolve(rootPath, copyFile.DestinationPath, out var copyPath, out var copyRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= copyRejection;

                        break;
                    }

                    if (packagePath is null)
                    {
                        throw new InvalidOperationException(
                            "La operación de copia del manifiesto necesita los bytes del paquete, y no están disponibles en disco.");
                    }

                    var directory = Path.GetDirectoryName(copyPath);

                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // AD-7, AD-9: la copia toma el CONTENIDO de una entrada del PAQUETE y
                    // lo escribe en un destino de la raíz, así que el destino se confina
                    // antes de escribir y el archivo sale de los bytes que el manifiesto
                    // firmado ya autorizó.
                    var written = await _extractor
                        .ExtractAsync(rootPath, packagePath, copyFile.SourcePath, copyFile.DestinationPath, destinationIsDirectory: false, cancellationToken)
                        .ConfigureAwait(false);

                    writtenBytes += written;

                    progress.Report(InstallPhase.ApplyingManifest, writtenBytes, totalBytes);

                    break;
                }

                case InstallationManifest.WriteConfig writeConfig:
                {
                    if (!TryResolve(rootPath, writeConfig.Path, out var configPath, out var configRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= configRejection;

                        break;
                    }

                    var directory = Path.GetDirectoryName(configPath);

                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var content = System.Text.Encoding.UTF8.GetBytes(writeConfig.Content);

                    await File.WriteAllBytesAsync(configPath, content, cancellationToken).ConfigureAwait(false);

                    writtenBytes += content.LongLength;

                    progress.Report(InstallPhase.ApplyingManifest, writtenBytes, totalBytes);

                    break;
                }

                case InstallationManifest.SetLaunchConfig setLaunchConfig:
                {
                    // La configuración de ejecución se MATERIALIZA dentro de la raíz, así
                    // que es una escritura y su ruta se confina como cualquier otra
                    // (AD-9, AC2).
                    var launchPath = "launch.json";

                    if (!TryResolve(rootPath, launchPath, out var materializedPath, out var launchRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= launchRejection;

                        break;
                    }

                    var directory = Path.GetDirectoryName(materializedPath);

                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var content = System.Text.Encoding.UTF8.GetBytes(
                        System.Text.Json.JsonSerializer.Serialize(
                            setLaunchConfig.Launch,
                            new System.Text.Json.JsonSerializerOptions(
                                System.Text.Json.JsonSerializerDefaults.Web)));

                    await File.WriteAllBytesAsync(materializedPath, content, cancellationToken).ConfigureAwait(false);

                    writtenBytes += content.LongLength;

                    progress.Report(InstallPhase.ApplyingManifest, writtenBytes, totalBytes);

                    break;
                }

                case InstallationManifest.Verify verify:
                {
                    if (!TryResolve(rootPath, verify.Path, out var verifiedPath, out var verifyRejection))
                    {
                        pathsConfinedToRoot = false;
                        rejectedPath ??= verifyRejection;

                        break;
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
                }

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

        // AC2, AC5: el decline de la confirmación deja la instalación FALLIDA con su
        // error persistido, así que el ejecutor lo dice con su propia excepción y el caso
        // de uso lo registra (AD-15, AD-25).
        if (declined)
        {
            throw new PrivilegedOperationDeclinedException(declinedOperation ?? "desconocida");
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
    /// The phase that the operation publishes while it runs (AC4).
    /// </summary>
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns>The phase of the operation.</returns>
    private static InstallPhase PhaseOf(InstallationManifest.ManifestOperation operation) => operation switch
    {
        InstallationManifest.Extract => InstallPhase.Extracting,
        InstallationManifest.Verify => InstallPhase.Validating,
        _ => InstallPhase.ApplyingManifest,
    };

    /// <summary>
    /// Names what the privileged operation writes, so the confirmation DICE lo que el
    /// jugador aprueba (AC2, UX-DR-16).
    /// </summary>
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns>The description of the operation.</returns>
    private static string Describe(InstallationManifest.ManifestOperation operation) => operation switch
    {
        InstallationManifest.WriteConfig writeConfig =>
            $"El manifiesto va a escribir el archivo de configuración '{writeConfig.Path}' dentro de la raíz de instalación.",

        InstallationManifest.SetLaunchConfig =>
            "El manifiesto va a materializar la configuración de ejecución del juego dentro de la raíz de instalación.",

        _ => "El manifiesto va a escribir dentro de la raíz de instalación.",
    };

    /// <summary>
    /// Estimates the total bytes that the operation expects to write, so the percentage
    /// tiene un denominador REAL (AC4).
    ///
    /// <para>
    /// El total es una ESTIMACIÓN a partir del tamaño del paquete y del contenido que el
    /// manifiesto declara, así que el porcentaje final puede no llegar al cien: la barra
    /// conserva su longitud REAL en todo momento (UX-DR-13).
    /// </para>
    /// </summary>
    /// <param name="manifest">Manifest whose operations are applied.</param>
    /// <param name="packagePath">Absolute path of the file that holds the bytes of the package.</param>
    /// <returns>The total bytes, or <c>null</c> when they cannot be estimated.</returns>
    private static long? EstimateTotalBytes(InstallationManifest manifest, string? packagePath)
    {
        long total = 0;

        foreach (var operation in manifest.Operations)
        {
            switch (operation)
            {
                case InstallationManifest.Extract extract:
                    total += ExtractSizeOf(packagePath, extract.ArchivePath);

                    break;

                case InstallationManifest.CopyFile copyFile:
                    total += FileSizeOf(packagePath, copyFile.SourcePath);

                    break;

                case InstallationManifest.WriteConfig writeConfig:
                    total += System.Text.Encoding.UTF8.GetByteCount(writeConfig.Content);

                    break;

                case InstallationManifest.SetLaunchConfig setLaunchConfig:
                    total += System.Text.Encoding.UTF8.GetByteCount(
                        System.Text.Json.JsonSerializer.Serialize(
                            setLaunchConfig.Launch,
                            new System.Text.Json.JsonSerializerOptions(
                                System.Text.Json.JsonSerializerDefaults.Web)));

                    break;

                default:
                    break;
            }
        }

        return total > 0 ? total : null;
    }

    /// <summary>
    /// The size in bytes of an entry of the package, or zero when it cannot be read
    /// (AC4).
    /// </summary>
    /// <param name="packagePath">Absolute path of the file that holds the bytes of the package.</param>
    /// <param name="archivePath">Path of the entry inside the package.</param>
    /// <returns>The size of the entry, in bytes.</returns>
    private static long ExtractSizeOf(string? packagePath, string archivePath)
    {
        if (packagePath is null || !File.Exists(packagePath))
        {
            return 0;
        }

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);

            return archive.GetEntry(archivePath)?.Length ?? 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (InvalidDataException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The size in bytes of a file of the package, or zero when it cannot be read (AC4).
    /// </summary>
    /// <param name="packagePath">Absolute path of the file that holds the bytes of the package.</param>
    /// <param name="sourcePath">Path of the file inside the package.</param>
    /// <returns>The size of the file, in bytes.</returns>
    private static long FileSizeOf(string? packagePath, string sourcePath)
    {
        if (packagePath is null || !File.Exists(packagePath))
        {
            return 0;
        }

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);

            return archive.GetEntry(sourcePath)?.Length ?? 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (InvalidDataException)
        {
            return 0;
        }
    }

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
    private bool TryResolve(
        string rootPath,
        string relativePath,
        out string resolved,
        out string? rejection)
    {
        resolved = string.Empty;
        rejection = null;

        try
        {
            // AD-9: el confine se comprueba contra la raíz que esta llamada está
            // ESCRIBIENDO, que durante una actualización es la raíz de PREPARACIÓN y no
            // la que hay en el disco (AD-9, AC4, FR-11).
            resolved = _paths.ResolveInside(rootPath, relativePath);

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

/// <summary>
/// Port that receives the PROGRESS of an installation operation (AC4).
///
/// <para>
/// El sumidero existe para que el ejecutor NO conozca la interfaz: el caso de uso
/// publica el progreso y la vista lo presenta (AD-1, AC-4).
/// </para>
/// </summary>
public interface IInstallProgressSink
{
    /// <summary>
    /// Reports the phase and the bytes REALES written so far (AC4).
    /// </summary>
    /// <param name="phase">Phase the operation is in.</param>
    /// <param name="bytesWritten">Bytes that have really been written so far.</param>
    /// <param name="totalBytes">Total bytes that the operation expects to write.</param>
    void Report(InstallPhase phase, long bytesWritten, long? totalBytes);
}

/// <summary>
/// Sumidero of the progress of an installation that KEEPS el último progreso publicado
/// (AC4).
///
/// <para>
/// El sumidero guarda el último estado para que la vista pueda leerlo cuando se cablea,
/// y su publicación es un AVISO, no una dependencia del hilo de la interfaz (AD-1).
/// </para>
/// </summary>
public sealed class RecordingInstallProgressSink : IInstallProgressSink
{
    private readonly Lock _gate = new();

    private InstallProgress _progress = InstallProgress.Idle;

    /// <summary>Raised when the progress changes and the view has to be repainted.</summary>
    public event Action<InstallProgress>? Changed;

    /// <summary>The last progress that was published.</summary>
    public InstallProgress Progress
    {
        get
        {
            lock (_gate)
            {
                return _progress;
            }
        }
    }

    /// <inheritdoc />
    public void Report(InstallPhase phase, long bytesWritten, long? totalBytes)
    {
        var progress = new InstallProgress(phase, bytesWritten, totalBytes);

        lock (_gate)
        {
            _progress = progress;
        }

        Changed?.Invoke(progress);
    }
}
