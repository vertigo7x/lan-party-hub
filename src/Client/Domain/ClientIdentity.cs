using System.Diagnostics.CodeAnalysis;

namespace LANPartyHub.Client.Domain;

/// <summary>
/// Technical identity of the client (AD-23).
///
/// <para>
/// The client emits the identifier once and persists it. It is an opaque UUID v7, and it
/// travels and it is persisted in its <see cref="CanonicalId"/> form: the 32 lowercase
/// hexadecimal digits of <c>Guid.ToString("N")</c>, without dashes. That is the very form
/// AD-31 uses to derive the effective name of the team, so the system carries one writing of
/// the identifier and not two that could diverge.
/// </para>
/// </summary>
public sealed class ClientIdentity
{
    /// <summary>Number of hexadecimal digits of the canonical form of the identifier.</summary>
    public const int CanonicalLength = 32;

    private ClientIdentity(Guid clientId)
    {
        ClientId = clientId;
    }

    /// <summary>The opaque identifier of the client.</summary>
    public Guid ClientId { get; }

    /// <summary>
    /// Canonical form of the identifier: 32 lowercase hexadecimal digits, without dashes
    /// (AD-23, AD-31).
    /// </summary>
    public string CanonicalId => ClientId.ToString("N");

    /// <summary>
    /// Emits a new identity. The value of the identifier is a UUID v7, so it sorts by the
    /// moment in which it was issued and it never repeats.
    /// </summary>
    public static ClientIdentity Emit()
    {
        // The runtime of .NET 10 exposes Guid.CreateVersion7(), so the identifier carries a
        // Unix millisecond timestamp, the version 7 and the RFC 9562 variant.
        return new ClientIdentity(Guid.CreateVersion7());
    }

    /// <summary>
    /// Rebuilds an identity out of its canonical form.
    /// </summary>
    /// <param name="canonicalId">Canonical form of the identifier.</param>
    /// <returns>The identity of the client.</returns>
    /// <exception cref="FormatException">The value is not a canonical identifier.</exception>
    public static ClientIdentity FromCanonical(string? canonicalId)
    {
        if (!IsCanonical(canonicalId) || !Guid.TryParseExact(canonicalId, "N", out var clientId))
        {
            throw new FormatException(
                $"La forma canónica del identificador del cliente son {CanonicalLength} dígitos hexadecimales en minúsculas y sin guiones.");
        }

        return new ClientIdentity(clientId);
    }

    /// <summary>
    /// Tells whether a value is the canonical form of an identifier: 32 lowercase hexadecimal
    /// digits and nothing else.
    /// </summary>
    /// <param name="canonicalId">Value to check.</param>
    /// <returns><c>true</c> when the value is canonical.</returns>
    public static bool IsCanonical([NotNullWhen(true)] string? canonicalId) =>
        canonicalId is { Length: CanonicalLength }
        && canonicalId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
