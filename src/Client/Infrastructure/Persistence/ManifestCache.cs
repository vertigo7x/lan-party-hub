using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Persistence;

/// <summary>
/// Adapter of the cache of the signed manifests of the client (AD-9, AD-20).
///
/// <para>
/// La caché vive en la carpeta <c>manifests/</c> de la carpeta de datos del cliente, y
/// un manifiesto se escribe en un archivo cuyo nombre es el identificador del paquete
/// (AD-9).
/// </para>
/// <para>
/// El caso de uso llama a esta operación SOLO después de que la firma y la versión del
/// esquema hayan pasado, así que un manifiesto que no es de fiar nunca llega al disco
/// (AD-2, AD-8, AD-27).
/// </para>
/// </summary>
public sealed class ManifestCache : IManifestCache
{
    /// <summary>Extension of the cached manifest files.</summary>
    public const string ManifestFileExtension = ".json";

    private readonly IClientDataLayout _layout;

    /// <summary>Creates the adapter of the cache of the signed manifests.</summary>
    /// <param name="layout">Layout of the data folder of the client.</param>
    public ManifestCache(IClientDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _layout = layout;
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string packageId,
        string canonicalManifest,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(canonicalManifest);

        var path = ManifestPath(packageId);

        Directory.CreateDirectory(_layout.ManifestsDirectory);

        // La escritura es ATÓMICA: el manifiesto se escribe en un archivo temporal y se
        // mueve encima del destino, así que una interrupción no deja un manifiesto a medio
        // escribir en la caché. El archivo temporal vive en la MISMA carpeta, porque el
        // movimiento tiene que ser del mismo volumen para que sea atómico.
        var temporary = path + ".tmp";

        try
        {
            await File.WriteAllTextAsync(temporary, canonicalManifest, cancellationToken).ConfigureAwait(false);

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Un temporal que se queda detrás no es un manifiesto válido: la caché solo
            // publica el archivo definitivo (AD-2).
            TryDeleteTemporary(temporary);

            throw;
        }
    }

    /// <summary>
    /// Deletes the temporary file of an interrupted write. A file that the operating system
    /// still holds does not fail the operation that is already failing (AD-15).
    /// </summary>
    /// <param name="path">Path of the temporary file.</param>
    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The temporary file stays behind and it is not read by the cache.
        }
        catch (UnauthorizedAccessException)
        {
            // The temporary file stays behind and it is not read by the cache.
        }
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var path = ManifestPath(packageId);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the path of the cached manifest of a package (AD-9).
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <returns>Absolute path of the cached manifest.</returns>
    private string ManifestPath(string packageId)
    {
        // El identificador de un paquete es un texto opaco y se usa como nombre de archivo,
        // así que no puede llevar un separador: este es el control que mantiene el
        // manifiesto DENTRO de la carpeta de la caché (AD-9).
        // La MISMA regla que el layout usa para el fichero parcial: un identificador
        // que uno acepta y el otro rechaza es un identificador que falla a medias
        // (AD-9).
        if (!ClientDataLayout.IsValidPackageId(packageId))
        {
            throw new ArgumentException(
                "El identificador de un paquete no puede usarse como nombre de archivo.",
                nameof(packageId));
        }

        return Path.Combine(_layout.ManifestsDirectory, packageId + ManifestFileExtension);
    }
}
