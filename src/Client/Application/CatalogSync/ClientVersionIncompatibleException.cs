using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.CatalogSync;

/// <summary>
/// Failure that says that the server rejected the client because its version is not
/// compatible (AD-6, FR-4).
///
/// <para>
/// La incompatibilidad NO es una caída del servidor: el servidor respondió, y su
/// respuesta fue un <c>426 Upgrade Required</c>. Por eso tiene su propia excepción y no
/// se mezcla con el fallo genérico de la red: el cliente incompatible NO aplica el
/// snapshot, NO descarga y NO instala (AD-6).
/// </para>
/// </summary>
public sealed class ClientVersionIncompatibleException : Exception
{
    /// <summary>
    /// Creates the failure of the incompatibility of the version.
    /// </summary>
    /// <param name="clientVersion">Version of the client that the server rejected.</param>
    /// <param name="serverVersion">Version of the server, when it published it.</param>
    public ClientVersionIncompatibleException(string clientVersion, string? serverVersion)
        : base($"El servidor rechazó la versión {clientVersion} del cliente porque no es compatible.")
    {
        ClientVersion = clientVersion;
        ServerVersion = serverVersion;
    }

    /// <summary>Version of the client that the server rejected.</summary>
    public string ClientVersion { get; }

    /// <summary>Version of the server, when it published it.</summary>
    public string? ServerVersion { get; }

    /// <summary>
    /// The error of the catalog that explains the incompatibility (AD-15).
    /// </summary>
    public ApiError Error => ErrorCatalog.Resolve(ErrorCatalog.ClientVersionIncompatibleCode);
}
