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
    public void The_view_keeps_the_four_projection_lists()
    {
        var view = new ClientRegistrationView(
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "EQ-PC",
            "1.0.0",
            1,
            ConnectionState.AnchorVerified,
            DateTimeOffset.UtcNow,
            [
                new ClientRegistrationView.InstalledGame(
                    "game-1",
                    "1.0.0",
                    StateDisplayMap.GetText(InstallationState.Installed)),
            ],
            [
                new ClientRegistrationView.ActiveOperation(
                    "operation-1",
                    "download",
                    StateDisplayMap.GetText(DownloadState.Downloading),
                    512,
                    2048),
            ],
            [
                new ClientRegistrationView.OperationOutcomeResult(
                    "operation-2",
                    StateDisplayMap.GetText(OperationOutcome.PackageWithdrawn)),
            ],
            [
                new ClientRegistrationView.RegisteredError(
                    "error-1",
                    "operation-3",
                    "PACKAGE_INTEGRITY_MISMATCH",
                    "message",
                    "cause",
                    "next step",
                    DateTimeOffset.UtcNow),
            ]);

        Assert.Single(view.InstalledGames);
        Assert.Single(view.ActiveOperations);
        Assert.Single(view.RecentOutcomes);
        Assert.Single(view.Errors);
    }

    [Fact]
    public void The_view_uses_empty_lists_when_the_projection_lists_are_null()
    {
        var view = new ClientRegistrationView(
            "0193f2a1-b2c3-4d4e-5f60-718293a4b5c6",
            "EQ-PC",
            "1.0.0",
            1,
            ConnectionState.AnchorVerified,
            DateTimeOffset.UtcNow,
            installedGames: null,
            activeOperations: null,
            recentOutcomes: null,
            errors: null);

        Assert.Empty(view.InstalledGames);
        Assert.Empty(view.ActiveOperations);
        Assert.Empty(view.RecentOutcomes);
        Assert.Empty(view.Errors);
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
