using LANPartyHub.Client.Application.Ports;

namespace LANPartyHub.Client.Infrastructure.Filesystem;

/// <summary>
/// Adapter of the LOCALIZATION of the package that was already VALIDATED (AD-9, AC1,
/// UX-DR-20).
///
/// <para>
/// El paquete validado vive en la carpeta de descargas <c>downloads/</c> de la carpeta de
/// datos del cliente, con el nombre que el layout declara para el archivo del paquete
/// (AD-9).
/// </para>
/// <para>
/// El adaptador NO lee los bytes ni calcula ningún hash: sólo localiza el archivo, así
/// que la decisión de reparabilidad sigue siendo de la certificación y de la regla pura
/// de la integridad (AC1, AD-5, AD-16).
/// </para>
/// </summary>
public sealed class ValidatedPackageStore : IValidatedPackageStore
{
    private readonly IClientDataLayout _layout;

    /// <summary>Creates the adapter of the localization of the validated package.</summary>
    /// <param name="layout">Layout of the data folder of the client.</param>
    public ValidatedPackageStore(IClientDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _layout = layout;
    }

    /// <inheritdoc />
    public string? Locate(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // El layout es el que decide el nombre del archivo del paquete y el que
        // comprueba que el archivo vive DENTRO de la carpeta de descargas (AD-9).
        var path = _layout.PartialFilePath(packageId);

        return File.Exists(path) ? path : null;
    }

    /// <inheritdoc />
    public string? LocateValidated(string packageId) => Locate(packageId);
}
