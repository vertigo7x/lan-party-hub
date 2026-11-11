using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Domain.Install;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Client.Infrastructure.Filesystem;

/// <summary>
/// Adapter of the PREPARATION and the SUSTITUTION of the installation root (AD-9, AC4,
/// FR-11).
///
/// <para>
/// La raíz de PREPARACIÓN es una HERMANA de la raíz activa: vive en el MISMO volumen,
/// así que la sustitución es un cambio de nombre, que en el mismo volumen es una
/// operación ATÓMICA (AD-9).
/// </para>
/// <para>
/// La raíz activa NO se borra hasta que la preparada ocupa su sitio: el cambio de
/// nombre de la activa a un lado y el de la preparada a su sitio son las dos
/// operaciones, y si la segunda falla, la primera se DESHACE, así que la instalación
/// anterior sigue vigente (AC4, FR-11).
/// </para>
/// </summary>
public sealed class InstallRootSwitcher : IInstallRootSwitcher
{
    /// <summary>
    /// Suffix of the folder of PREPARATION, that is a sibling of the active root.
    /// </summary>
    public const string StagingSuffix = ".preparacion";

    /// <summary>
    /// Suffix of the folder that holds the root REPLACED, while the new one takes its
    /// place.
    /// </summary>
    public const string ReplacedSuffix = ".sustituida";

    private readonly ILogger<InstallRootSwitcher> _logger;

    /// <summary>Creates the adapter of the substitution of the installation root.</summary>
    /// <param name="logger">Log of the adapter.</param>
    public InstallRootSwitcher(ILogger<InstallRootSwitcher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public string PrepareStagingRoot(string activeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeRoot);

        var active = Path.GetFullPath(activeRoot);

        // AD-9: la raíz de PREPARACIÓN es HERMANA de la activa, nunca una carpeta dentro
        // de ella: una carpeta dentro no se puede intercambiar con su madre, y el
        // intercambio es lo que deja la instalación anterior intacta hasta el final
        // (AD-9, AC4).
        var staging = Path.Combine(
            Path.GetDirectoryName(active) ?? active,
            Path.GetFileName(active) + StagingSuffix);

        // La raíz de preparación empieza VACÍA: el árbol nuevo no puede heredar ni un
        // archivo de la versión anterior (AC4).
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        _logger.LogInformation(
            "Se prepara la raíz de instalación nueva en '{StagingRoot}', fuera de la raíz activa '{ActiveRoot}'.",
            staging,
            active);

        return staging;
    }

    /// <inheritdoc />
    public string CommitStagingRoot(string activeRoot, string stagingRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        var active = Path.GetFullPath(activeRoot);
        var staging = Path.GetFullPath(stagingRoot);

        if (string.Equals(active, staging, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La raíz de preparación tiene que ser DISTINTA de la raíz activa.");
        }

        if (!Directory.Exists(staging))
        {
            throw new DirectoryNotFoundException(
                $"La raíz de preparación '{staging}' no existe, así que la instalación nueva no se puede publicar.");
        }

        var replaced = Path.Combine(
            Path.GetDirectoryName(active) ?? active,
            Path.GetFileName(active) + ReplacedSuffix);

        DeleteDirectory(replaced);

        // El primer cambio de nombre APARTA la raíz activa: a partir de aquí la versión
        // anterior está a un lado y no se toca más (AC4).
        var activeExisted = Directory.Exists(active);

        if (activeExisted)
        {
            Directory.Move(active, replaced);
        }

        try
        {
            // El segundo cambio de nombre PUBLICA la raíz nueva en el sitio de la activa
            // (AC1, FR-11).
            Directory.Move(staging, active);
        }
        catch (Exception exception)
        {
            // La publicación falló, así que la instalación anterior VUELVE a su sitio: un
            // fallo de la versión nueva no puede dejar al jugador sin juego (AC4).
            if (activeExisted && Directory.Exists(replaced) && !Directory.Exists(active))
            {
                Directory.Move(replaced, active);
            }

            _logger.LogError(
                exception,
                "No se pudo publicar la raíz de instalación nueva en '{ActiveRoot}': la instalación anterior conserva su sitio.",
                active);

            throw;
        }

        // La raíz apartada ya no hace falta: la versión nueva está publicada y es la que
        // cuenta (AC1).
        DeleteDirectory(replaced);

        _logger.LogInformation(
            "La raíz de instalación '{ActiveRoot}' publica la versión nueva y la anterior queda descartada.",
            active);

        return replaced;
    }

    /// <inheritdoc />
    public void DiscardStagingRoot(string activeRoot, string stagingRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        var active = Path.GetFullPath(activeRoot);
        var staging = Path.GetFullPath(stagingRoot);

        // AC4: el descarte NUNCA toca la raíz activa. La garantía de que un fallo de la
        // versión nueva no invalida la instalación anterior está aquí (AC4, FR-11).
        if (string.Equals(active, staging, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La raíz de preparación tiene que ser DISTINTA de la raíz activa.");
        }

        DeleteDirectory(staging);

        _logger.LogInformation(
            "La raíz de preparación '{StagingRoot}' se descarta: la raíz activa '{ActiveRoot}' conserva su contenido.",
            staging,
            active);
    }

    /// <summary>
    /// Deletes a directory and EVERYTHING inside it, and never fails because of it: un
    /// resto que no se puede borrar no puede INVALIDAR la instalación que ya es válida
    /// (AC4).
    /// </summary>
    /// <param name="path">Absolute path of the directory to delete.</param>
    private void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "No se pudo borrar la carpeta '{Path}': el resto no afecta a la instalación vigente.",
                path);
        }
    }
}
