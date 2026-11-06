using System.Text.Json;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using LANPartyHub.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Install;

/// <summary>
/// Adapter of the INSPECTION of an installation before launching a videogame (AD-19,
/// AD-9, AD-2, AC1).
///
/// <para>
/// La configuración de lanzamiento que entrega es la del MANIFIESTO FIRMADO, y el
/// manifiesto se lee de la caché <c>manifests/</c>, que sólo guarda manifiestos cuya
/// firma validó contra el ancla fijada (AD-2, AD-8).
/// </para>
/// <para>
/// La certificación la produce la REGLA PURA del dominio —<c>InstallationCertification</c>—
/// y este adaptador sólo le entrega las TRES CONDICIONES que la regla evalúa, así que no
/// existe una segunda definición de «instalación válida» (AC5, AD-1).
/// </para>
/// <para>
/// La ruta del ejecutable NUNCA sale de la raíz de instalación: el confine de AD-9 se
/// aplica también sobre la ruta que el manifiesto declara (AD-9, AC1).
/// </para>
/// </summary>
public sealed class InstallationInspector : IInstallationInspector
{
    private static readonly JsonSerializerOptions ManifestJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IClientStateStore _stateStore;
    private readonly ICatalogSnapshotStore _snapshots;
    private readonly IManifestCache _manifestCache;
    private readonly IInstallPathResolver _paths;
    private readonly ILogger<InstallationInspector> _logger;

    /// <summary>Creates the adapter of the inspection of an installation.</summary>
    /// <param name="stateStore">Access to the database of the client.</param>
    /// <param name="snapshots">Port of the persistence of the snapshot of the catalog.</param>
    /// <param name="manifestCache">Port of the cache of the signed manifests.</param>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <param name="logger">Log of the adapter.</param>
    public InstallationInspector(
        IClientStateStore stateStore,
        ICatalogSnapshotStore snapshots,
        IManifestCache manifestCache,
        IInstallPathResolver paths,
        ILogger<InstallationInspector> logger)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(manifestCache);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _stateStore = stateStore;
        _snapshots = snapshots;
        _manifestCache = manifestCache;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string RootPath => _paths.RootPath;

    /// <inheritdoc />
    public async Task<InstallationInspection?> InspectAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var registered = await _stateStore
            .LoadInstallationAsync(gameId, cancellationToken)
            .ConfigureAwait(false);

        if (registered is null)
        {
            return null;
        }

        var manifest = await LoadSignedManifestAsync(gameId, cancellationToken).ConfigureAwait(false);

        // Sin manifiesto FIRMADO no hay configuración de lanzamiento confiable: la
        // instalación NO se puede certificar, porque la firma es la que da la confianza
        // (AD-2).
        if (manifest is null)
        {
            _logger.LogWarning(
                "El cliente no tiene un manifiesto firmado para el juego {GameId}, así que la instalación no se puede certificar.",
                gameId);

            return new InstallationInspection(
                registered.InstallPath,
                NotCertified(),
                new InstallationManifest.LaunchSettings(string.Empty, [], string.Empty));
        }

        var verifyPassed = VerifyInstalledTree(manifest);
        var pathsConfinedToRoot = PathsAreConfined(manifest);

        // AC5, AD-1: la certificación es la ÚNICA definición de instalación válida, y las
        // TRES condiciones se evaluan JUNTAS. La primera condición se afirma porque el
        // manifiesto que se lee es el FIRMADO, y su paquete validó contra el ancla
        // fijada (AD-2, AD-8).
        var verdict = InstallationCertification.Certify(
            packageValidatedAgainstAnchor: true,
            manifestVerifyPassed: verifyPassed,
            pathsConfinedToRoot: pathsConfinedToRoot,
            repairablePackageAvailable: false);

        return new InstallationInspection(registered.InstallPath, verdict, manifest.Launch);
    }

    /// <summary>
    /// Reads the SIGNED manifest of the package that belongs to a videogame (AD-2).
    ///
    /// <para>
    /// El manifiesto se busca por el PAQUETE del juego, porque la caché de manifiestos
    /// está indexada por identificador de paquete (AD-9).
    /// </para>
    /// </summary>
    /// <param name="gameId">Identifier of the videogame.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The signed manifest, or <c>null</c> when the client has none.</returns>
    private async Task<InstallationManifest?> LoadSignedManifestAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _snapshots.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            return null;
        }

        var package = snapshot.Packages.FirstOrDefault(candidate =>
            string.Equals(candidate.GameId, gameId, StringComparison.Ordinal));

        if (package is null)
        {
            return null;
        }

        var canonical = await _manifestCache
            .LoadAsync(package.PackageId, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(canonical))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallationManifest>(canonical, ManifestJsonOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "El manifiesto firmado en caché del paquete {PackageId} no se puede leer: la instalación no se puede certificar.",
                package.PackageId);

            return null;
        }
    }

    /// <summary>
    /// Tells whether the <c>verify</c> operation of the manifest passes over the
    /// installed tree (AD-7, AC5).
    ///
    /// <param name="manifest">Signed manifest of the package.</param>
    /// <returns><c>true</c> when the tree passes the verification.</returns>
    private bool VerifyInstalledTree(InstallationManifest manifest)
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
    /// Tells whether EVERY path that the manifest declares stays inside the installation
    /// root (AD-9, AC5).
    ///
    /// <param name="manifest">Signed manifest of the package.</param>
    /// <returns><c>true</c> when every path is confined.</returns>
    private bool PathsAreConfined(InstallationManifest manifest)
    {
        foreach (var operation in manifest.Operations)
        {
            foreach (var path in PathsOf(operation))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (!_paths.IsInsideRoot(path))
                {
                    return false;
                }
            }
        }

        // La ruta del EJECUTABLE también es una ruta del manifiesto, así que el confine se
        // aplica a ella igual (AD-9, AC1).
        var executable = manifest.Launch?.ExecutablePath;

        if (!string.IsNullOrWhiteSpace(executable) && !_paths.IsInsideRoot(executable))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the paths that an operation of the manifest declares (AD-9).
    ///
    /// <param name="operation">Operation of the manifest.</param>
    /// <returns>The paths that the operation declares.</returns>
    private static IEnumerable<string> PathsOf(InstallationManifest.ManifestOperation operation) => operation switch
    {
        InstallationManifest.Extract extract => [extract.ArchivePath, extract.DestinationPath],
        InstallationManifest.CreateDirectory create => [create.Path],
        InstallationManifest.CopyFile copy => [copy.SourcePath, copy.DestinationPath],
        InstallationManifest.WriteConfig write => [write.Path],
        InstallationManifest.Verify verify => [verify.Path],
        _ => [],
    };

    /// <summary>
    /// The verdict of an installation that CANNOT be certified, con su motivo visible
    /// (AC5, AC6).
    /// </summary>
    /// <returns>The verdict of an installation that is not certified.</returns>
    private static InstallationVerdict NotCertified() => InstallationCertification.Certify(
        packageValidatedAgainstAnchor: false,
        manifestVerifyPassed: false,
        pathsConfinedToRoot: false,
        repairablePackageAvailable: false);
}
