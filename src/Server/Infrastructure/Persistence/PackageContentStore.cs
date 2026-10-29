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

        var path = Path.Combine(_dataDirectory.PackagesPath, packageId);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
