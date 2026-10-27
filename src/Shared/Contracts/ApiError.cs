namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// The single API error shape of the whole system (AD-15, AD-18, AR-4).
/// It is the only type that may be serialized in a REST error body and the only one
/// that may travel in the <see cref="ClientErrorReported"/> payload.
/// </summary>
/// <param name="Code">Stable identifier taken from <see cref="ErrorCatalog"/>. Never the primary text.</param>
/// <param name="Message">What failed.</param>
/// <param name="Cause">Why it failed. Mandatory.</param>
/// <param name="NextStep">What to do now. Mandatory.</param>
public sealed record ApiError(string Code, string Message, string Cause, string NextStep);
