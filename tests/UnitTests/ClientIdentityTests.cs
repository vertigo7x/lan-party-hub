using LANPartyHub.Client.Domain;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// Identity of the client (AD-23, AD-31).
///
/// <para>
/// The checks of the version and of the variant are written here, and not taken from the
/// code that emits the identifier, so the assertion does not depend on the implementation it
/// checks.
/// </para>
/// </summary>
public sealed class ClientIdentityTests
{
    [Fact]
    public void The_emitted_identifier_is_a_uuid_of_the_version_seven()
    {
        var identity = ClientIdentity.Emit();

        Assert.True(IsVersion7(identity.ClientId), "The emitted identifier is not a UUID v7.");
        Assert.True(HasRfc4122Variant(identity.ClientId), "The emitted identifier does not have the RFC 4122 variant.");
    }

    [Fact]
    public void The_emitted_identifier_carries_the_moment_in_which_it_was_issued()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var identity = ClientIdentity.Emit();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var issuedAt = ReadUnixMilliseconds(identity.ClientId);

        Assert.InRange(issuedAt, before, after);
    }

    [Fact]
    public void The_canonical_form_is_thirty_two_lowercase_hexadecimal_digits_without_dashes()
    {
        var identity = ClientIdentity.Emit();

        Assert.Equal(ClientIdentity.CanonicalLength, identity.CanonicalId.Length);
        Assert.Matches("^[0-9a-f]{32}$", identity.CanonicalId);
        Assert.Equal(identity.ClientId.ToString("N"), identity.CanonicalId);
    }

    [Fact]
    public void Two_emissions_are_different()
    {
        var first = ClientIdentity.Emit();
        var second = ClientIdentity.Emit();

        Assert.NotEqual(first.CanonicalId, second.CanonicalId);
    }

    [Fact]
    public void A_canonical_form_is_rebuilt_into_the_same_identifier()
    {
        var identity = ClientIdentity.Emit();

        var rebuilt = ClientIdentity.FromCanonical(identity.CanonicalId);

        Assert.Equal(identity.ClientId, rebuilt.ClientId);
        Assert.Equal(identity.CanonicalId, rebuilt.CanonicalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0193f2a1b2c34d4e5f60718293a4b5c")]
    [InlineData("0193f2a1b2c34d4e5f60718293a4b5c6f")]
    [InlineData("0193F2A1B2C34D4E5F60718293A4B5C6")]
    [InlineData("0193f2a1-b2c3-4d4e-5f60-718293a4b5c6")]
    [InlineData("zzz3f2a1b2c34d4e5f60718293a4b5c6")]
    public void A_value_that_is_not_canonical_is_rejected(string? value)
    {
        Assert.False(ClientIdentity.IsCanonical(value));

        Assert.Throws<FormatException>(() => ClientIdentity.FromCanonical(value!));
    }

    /// <summary>
    /// Tells whether the version nibble of a UUID is the seven. The layout of the identifier
    /// is the one of RFC 9562: the version sits in the high nibble of the seventh octet.
    /// </summary>
    private static bool IsVersion7(Guid clientId)
    {
        var bytes = clientId.ToByteArray();

        return (bytes[7] & 0xF0) == 0x70;
    }

    /// <summary>
    /// Tells whether the variant of a UUID is the one of RFC 4122, which sets the two high
    /// bits of the ninth octet.
    /// </summary>
    private static bool HasRfc4122Variant(Guid clientId)
    {
        var bytes = clientId.ToByteArray();

        return (bytes[8] & 0xC0) == 0x80;
    }

    /// <summary>
    /// Reads the 48 bits of the Unix timestamp in milliseconds that a UUID v7 carries in its
    /// first six octets.
    /// </summary>
    private static long ReadUnixMilliseconds(Guid clientId)
    {
        var bytes = clientId.ToByteArray();

        return (long)bytes[3] << 40
            | (long)bytes[2] << 32
            | (long)bytes[1] << 24
            | (long)bytes[0] << 16
            | (long)bytes[5] << 8
            | bytes[4];
    }
}
