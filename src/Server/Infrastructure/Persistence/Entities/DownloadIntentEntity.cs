namespace LANPartyHub.Server.Infrastructure.Persistence.Entities;

/// <summary>
/// Row of the <c>DOWNLOAD_INTENT</c> table (AD-22, AD-10, AD-33).
///
/// The server records the intention of a download, not the state of the operation: the
/// operation is executed and owned by the client (AD-10). The state is the name of the value
/// of the machine state and the outcome is the name of the value of the terminal result
/// (AD-33); they are two different things and neither replaces the other.
/// </summary>
public sealed class DownloadIntentEntity
{
    /// <summary>Opaque identifier of the operation.</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Opaque UUID v7 identifier of the client that registered the intention.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Opaque UUID v7 identifier of the package of the intention.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Name of the value of the machine state of the operation.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Name of the value of the terminal result of the operation.</summary>
    public string Outcome { get; set; } = string.Empty;
}
