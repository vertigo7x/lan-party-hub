namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Error reported by the client to the server (AD-18, AD-22). The only error shape that may
/// travel in this payload is <see cref="ApiError"/>.
/// </summary>
/// <param name="ClientId">Opaque UUID v7 identifier of the client.</param>
/// <param name="OperationId">Opaque UUID v7 identifier of the operation, empty when the error is not bound to one.</param>
/// <param name="Error">The error, with its catalog code, cause and next step.</param>
public sealed record ClientErrorReported(string ClientId, string OperationId, ApiError Error);
