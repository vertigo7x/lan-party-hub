using LANPartyHub.Server.Application.Ports;
using Microsoft.Extensions.Logging;

namespace LANPartyHub.Server.Application.Packages;

/// <summary>
/// Use case that reads the signed installation manifest of a published package
/// (AD-2, AD-8, AD-20, AD-22).
///
/// <para>
/// El manifiesto firmado viaja APARTE de los bytes del paquete, y el cliente lo pide
/// ANTES de descargar: la firma y la versión del esquema se validan antes de que exista
/// un archivo parcial (AD-8, AD-20).
/// </para>
/// <para>
/// El caso de uso devuelve el texto CANÓNICO del manifiesto tal y como se firmó, así que
/// los bytes que el cliente verifica son exactamente los bytes que la firma cubre: no hay
/// dos canonicalizadores que puedan divergir (AD-2).
/// </para>
/// <para>
/// El caso de uso depende del puerto declarado en esta capa y nunca del adaptador que lo
/// implementa (AD-1).
/// </para>
/// </summary>
public sealed class GetSignedManifest
{
    private readonly IPackageRepository _packages;
    private readonly ILogger<GetSignedManifest> _logger;

    /// <summary>Creates the use case that reads the signed manifest of a package.</summary>
    /// <param name="packages">Port of the repository of the published packages.</param>
    /// <param name="logger">Log of the use case.</param>
    public GetSignedManifest(IPackageRepository packages, ILogger<GetSignedManifest> logger)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(logger);

        _packages = packages;
        _logger = logger;
    }

    /// <summary>
    /// Reads the signed manifest of a published package.
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package whose manifest is read.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>
    /// The signed manifest of the package, or <c>null</c> when the server does not hold a
    /// published package with that identifier.
    /// </returns>
    public async Task<SignedManifestRow?> ExecuteAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var manifest = await _packages
            .FindManifestAsync(packageId, cancellationToken)
            .ConfigureAwait(false);

        if (manifest is null)
        {
            _logger.LogInformation(
                "El servidor no tiene manifiesto firmado para el paquete {PackageId}.",
                packageId);

            return null;
        }

        _logger.LogInformation(
            "Se sirve el manifiesto firmado del paquete {PackageId} con la clave {KeyId}.",
            manifest.PackageId,
            manifest.KeyId);

        return manifest;
    }
}
