namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Installation progress pushed by the client to the server (AD-18, AD-22).
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client.</param>
/// <param name="OperationId">Opaque UUID v7 identifier of the installation operation.</param>
/// <param name="State">Current installation machine state.</param>
/// <param name="Phase">Current phase of the installation, for example the extraction phase.</param>
public sealed record InstallationProgressUpdated(
    string ClientId,
    string OperationId,
    InstallationState State,
    string Phase);
