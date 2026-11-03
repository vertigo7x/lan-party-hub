using System.Globalization;
using LANPartyHub.Shared.Contracts;

namespace LANPartyHub.Client.Application.Connection;

/// <summary>
/// The single rule that turns the text of an address into the contract of the endpoint
/// (AC1, AC3, AD-3).
///
/// <para>
/// The accepted format is <c>host:puerto</c>: the host is not empty and the port is an
/// integer in the range 1-65535. The parser lives in the Application layer because the
/// manual way of connecting and the flags of the startup share the SAME rule: the address
/// of the player is read in the two places and it has to mean the same thing in both
/// (AC1, AD-3).
/// </para>
/// <para>
/// A malformed address NEVER throws: it resolves into an <see cref="ApiError"/> of the
/// catalog with its three parts, and the internal code is never the primary text of the
/// message (AC3, UX-DR-36, AR-4).
/// </para>
/// </summary>
public static class ServerEndpointParser
{
    /// <summary>
    /// Expected format of the address of the event server. The host and the port travel
    /// apart in the contract <see cref="ServerEndpoint"/> of <c>Shared</c>, and the
    /// address of the player carries them together in this form.
    /// </summary>
    public const string ServerAddressFormat = "host:puerto";

    /// <summary>Lowest valid TCP port.</summary>
    public const int MinimumPort = 1;

    /// <summary>Highest valid TCP port.</summary>
    public const int MaximumPort = 65535;

    /// <summary>
    /// Parses the text of an address into the contract of the endpoint (AC1).
    ///
    /// <para>
    /// The separator is the last colon, so a literal of IPv6 written between brackets is
    /// read as one host and not as a port.
    /// </para>
    /// </summary>
    /// <param name="value">Text of the address.</param>
    /// <param name="endpoint">Endpoint that the text describes, or <c>null</c> when the text is malformed.</param>
    /// <param name="error">
    /// Error of the catalog that explains what failed, why and what to do now, or
    /// <c>null</c> when the text is valid.
    /// </param>
    /// <returns><c>true</c> when the text is a valid address.</returns>
    public static bool TryParse(
        string? value,
        out ServerEndpoint? endpoint,
        out ApiError? error)
    {
        endpoint = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = ErrorCatalog.Resolve(ErrorCatalog.ServerAddressInvalidCode);

            return false;
        }

        var separator = value.LastIndexOf(':');

        if (separator <= 0)
        {
            error = ErrorCatalog.Resolve(ErrorCatalog.ServerAddressInvalidCode);

            return false;
        }

        var hostPart = value[..separator];
        var portPart = value[(separator + 1)..];

        if (hostPart.Length == 0 || hostPart.Any(char.IsWhiteSpace))
        {
            error = ErrorCatalog.Resolve(ErrorCatalog.ServerAddressInvalidCode);

            return false;
        }

        if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port < MinimumPort
            || port > MaximumPort)
        {
            error = ErrorCatalog.Resolve(ErrorCatalog.ServerAddressInvalidCode);

            return false;
        }

        endpoint = new ServerEndpoint(hostPart, port);
        error = null;

        return true;
    }
}
