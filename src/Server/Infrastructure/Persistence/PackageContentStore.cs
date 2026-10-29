using LANPartyHub.Server.Application.Ports;

namespace LANPartyHub.Server.Infrastructure.Persistence;

/// <summary>
/// Adapter of the reading of the bytes of the published packages (AD-1, AD-7, AD-22).
///
/// <para>
/// The bytes of a published package live in the repository of packages that the data
/// directory of the server holds, and this adapter is the only one that knows that folder
/// (AD-17, AD-22). The use case that publishes a package receives the bytes through the
/// port <see cref="IPackageContentStore"/> and it never learns where they live (AD-1).
/// </para>
/// </summary>
public sealed class PackageContentStore : IPackageContentStore
{
    private readonly DataDirectory _dataDirectory;

    /// <summary>Creates the adapter of the reading of the bytes of the published packages.</summary>
    /// <param name="dataDirectory">Data folder of the server, with the repository of packages.</param>
    public PackageContentStore(DataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);

        _dataDirectory = dataDirectory;
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadBytesAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // The identifier of the package is the name of a file inside the repository of
        // packages, and it comes from the body of the request. An identifier that is not
        // a plain file name is not a package of this repository: a name with a separator
        // or with a ".." would escape the repository, and a rooted name would make the
        // combination of paths return that absolute path, so the server would read a file
        // that has nothing to do with the catalog and publish its hash (AD-9, AD-22).
        if (!IsPlainFileName(packageId))
        {
            return null;
        }

        var path = Path.Combine(_dataDirectory.PackagesPath, packageId);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells whether a value is the plain name of a file, with no directory part and no
    /// relative path (AD-9).
    /// </summary>
    /// <param name="value">Value to check.</param>
    /// <returns><c>true</c> when the value is a plain file name.</returns>
    private static bool IsPlainFileName(string value)
    {
        if (value is "." or "..")
        {
            return false;
        }

        if (Path.IsPathRooted(value))
        {
            return false;
        }

        if (value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || value.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        // A name that carries a segment of ".." is not a plain file name: it is a
        // relative path that can climb out of the repository. The name is split by the
        // separators of the platform, and the only separator that can be inside a name
        // is the dot of the extension, which never forms a ".." segment.
        foreach (var segment in value.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals("..", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
