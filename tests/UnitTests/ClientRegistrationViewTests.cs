using LANPartyHub.Shared.Contracts;
using Xunit;

namespace LANPartyHub.UnitTests;

/// <summary>
/// The view of a registered client (AD-18, AD-29). The dates are UTC (spine, convención
/// Data &amp; formats) and the machine name is the effective one, never the proposed one
/// that collided (AD-30).
/// </summary>
public sealed class ClientRegistrationViewTests
{
    [Fact]
    public void The_last_seen_instant_is_normalized_to_utc()
    {
        var local = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(2));

        var view = new ClientRegistrationView(
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "EQ-PC",
            "1.0.0",
            1,
            ConnectionState.AnchorVerified,
            local);

        Assert.Equal(TimeSpan.Zero, view.LastSeenAt.Offset);
        Assert.Equal(local.ToUniversalTime(), view.LastSeenAt);
    }

    [Fact]
    public void The_view_keeps_the_effective_machine_name_and_the_connection_state()
    {
        var view = new ClientRegistrationView(
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c6",
            "1.0.0",
            1,
            ConnectionState.AnchorUnverified,
            DateTimeOffset.UtcNow);

        Assert.Equal("EQ-PC-0193f2a1b2c34d4e5f60718293a4b5c6", view.MachineName);
        Assert.Equal(ConnectionState.AnchorUnverified, view.ConnectionState);
    }
}
