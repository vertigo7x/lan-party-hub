using System.IO.Compression;
using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Filesystem;

/// <summary>
/// Adapter of the extraction of an archive of the package into the installation root
/// (AD-7, AD-9, AC1).
///
/// <para>
/// La extracción escribe SÓLO dentro de la raíz configurada: el destino se resuelve
/// contra la raíz y la ruta canónica se COMPRUEBA antes de CADA entrada del archivo, así que un
/// archivo con una entrada que sale de la raíz no escribe nada fuera (AD-9).
/// </para>
/// <para>
/// El <c>zip-slip</c> cae en el CONFINÉ: una entrada que declara una ruta con <c>..</c> o
/// absoluta NO se escribe fuera de la raíz, y la operación se rechaza con el diagnóstico
/// del catálogo (AD-9, AC1).
/// </para>
/// <para>
/// Los bytes que se escriben son los REALES de la entrada, así que el porcentaje del
/// progreso sale de la longitud de la entrada y no de una estimación (AC4).
/// </para>
/// </summary>
public sealed class ZipInstallArchiveExtractor : IInstallArchiveExtractor
{
    private readonly IInstallPathResolver _paths;

    /// <summary>Creates the adapter of the extraction of the archives.</summary>
    /// <param name="paths">Resolver of the paths of the installation.</param>
    /// <exception cref="ArgumentNullException">The resolver is <c>null</c>.</exception>
    public ZipInstallArchiveExtractor(IInstallPathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<long> ExtractAsync(
        string packagePath,
        string archivePath,
        string destinationPath,
        bool destinationIsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // AD-9: el destino se resuelve contra la raíz ANTES de abrir el archivo, así que
        // un destino que sale de la raíz se rechaza sin tocar el disco.
        var resolvedDestination = _paths.ResolveInsideRoot(destinationPath);

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                $"No hay bytes del paquete en '{packagePath}' para extraer la entrada '{archivePath}'.",
                packagePath);
        }

        await using var archive = ZipFile.OpenRead(packagePath);

        var entry = archive.GetEntry(archivePath);

        if (entry is null)
        {
            throw new InvalidDataException(
                $"El paquete '{packagePath}' no declara la entrada '{archivePath}' del manifiesto.");
        }

        // AD-9, AC1: el NOMBRE de la entrada también se confina. Una entrada con un `..`
        // en su nombre es un `zip-slip`, y el destino que declara el manifiesto NO le
        // da permiso para salir de la raíz.
        var entryName = Path.GetFileName(entry.FullName);

        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw new InvalidDataException(
                $"La entrada '{archivePath}' del paquete no declara un nombre de archivo válido.");
        }

        var targetPath = destinationIsDirectory
            ? Path.Combine(resolvedDestination, entryName)
            : resolvedDestination;

        // AD-9: el destino FINAL se resuelve otra vez, así que una ruta que sale de la
        // raíz —por el nombre de la entrada o por el destino del manifiesto— se rechaza
        // ANTES de escribir.
        var canonicalDestination = destinationIsDirectory
            ? _paths.ResolveInsideRoot(Path.Combine(destinationPath, entryName))
            : _paths.ResolveInsideRoot(destinationPath);

        var directory = Path.GetDirectoryName(canonicalDestination);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = entry.Open();
        await using var target = new FileStream(
            canonicalDestination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var written = await CopyAsync(source, target, cancellationToken).ConfigureAwait(false);

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        // AC4: los bytes REALES escritos son los que miden el progreso.
        return written;
    }

    /// <summary>
    /// Copies the bytes of an entry into a file and COUNTS the ones that were really
    /// written (AC4).
    /// </summary>
    /// <param name="source">Stream of the entry of the archive.</param>
    /// <param name="target">Stream of the file that is written.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The number of bytes that were written.</returns>
    private static async Task<long> CopyAsync(
        Stream source,
        Stream target,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long written = 0;

        while (true)
        {
            var read = await source
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            written += read;
        }

        return written;
    }
}
