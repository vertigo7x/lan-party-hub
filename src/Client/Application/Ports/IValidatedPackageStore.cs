namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the LOCALIZATION of the package that was already VALIDATED (AD-9, AC1,
/// UX-DR-20).
///
/// <para>
/// El puerto existe para que el caso de uso de la REPARACIÓN pueda re-verificar el
/// <c>sha256</c> del paquete validado sin conocer el disco: el puerto dice DÓNDE está
/// el paquete y el adaptador lo busca en la carpeta de descargas (AD-1, AD-16).
/// </para>
/// <para>
/// La reparación es una operación de CERO RED: el paquete ya validado y el manifiesto
/// firmado en caché son LOCALES, así que la reparación no llama al puerto de la
/// intención de descarga ni al motor de transferencia (AC1, UX-DR-20).
/// </para>
/// </summary>
public interface IValidatedPackageStore
{
    /// <summary>
    /// Locates the file that holds the bytes of the package that was already validated
    /// (AD-9, AC1).
    ///
    /// <para>
    /// El puerto NO decide si el paquete conserva su hash: eso lo decide la regla pura
    /// de la integridad, que compara el <c>sha256</c> calculado con el que declara el
    /// manifiesto FIRMADO (AD-2, AD-8).
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <returns>
    /// The absolute path of the package, or <c>null</c> when the client does NOT hold the
    /// validated package of that identifier on disk.
    /// </returns>
    string? Locate(string packageId);

    /// <summary>
    /// Locates the file that holds the bytes of the package that was already validated,
    /// and says whether the client still HOLDS it (AC1, UX-DR-20).
    ///
    /// <para>
    /// La diferencia entre esta operación y <see cref="Locate"/> es la que decide si la
    /// instalación sigue siendo REPARABLE: sin el paquete validado en disco no hay
    /// reparación, y el motivo visible es el de la re-descarga (AC1, AC2).
    /// </para>
    /// </summary>
    /// <param name="packageId">Opaque identifier of the package.</param>
    /// <returns>
    /// The absolute path of the package, or <c>null</c> when the client does NOT hold the
    /// validated package of that identifier on disk.
    /// </returns>
    string? LocateValidated(string packageId);
}

/// <summary>
/// The package that was already VALIDATED for an installation, as the repair consumes it
/// (AC1, UX-DR-20).
///
/// <para>
/// La reparación necesita el IDENTIFICADOR del paquete para leer su manifiesto firmado
/// en caché, así que el puerto entrega las DOS cosas: el identificador y la ruta del
/// archivo que holds los bytes (AC1, AD-9).
/// </para>
/// </summary>
/// <param name="PackageId">Opaque identifier of the package that was validated.</param>
/// <param name="Path">
/// Absolute path of the file that holds the bytes of the package, or <c>null</c> when the
/// client does NOT hold it on disk.
/// </param>
public sealed record ValidatedPackage(string PackageId, string? Path);
