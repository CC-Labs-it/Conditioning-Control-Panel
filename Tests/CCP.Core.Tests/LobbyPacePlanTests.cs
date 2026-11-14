using System;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The pure half of WPF LobbyPaceTests (main c3390d90ea): LobbyPollPlan lives in Core.
/// The lease and race-hold tests stay in the WPF suite, on WPF LobbyService.</summary>
public sealed class LobbyPacePlanTests
{
    [Fact]
    public void Badge_only_waits_the_badge_pace_whatever_the_answer()
    {
        Assert.Equal(LobbyPollPlan.Badge, LobbyPollPlan.Next(LobbyPollPlan.Base, ok: true, badgeOnly: true));
        Assert.Equal(LobbyPollPlan.Badge, LobbyPollPlan.Next(LobbyPollPlan.Max, ok: false, badgeOnly: true));
        Assert.Equal(LobbyPollPlan.Base, LobbyPollPlan.Next(LobbyPollPlan.Max, ok: true));
        Assert.True(LobbyPollPlan.Badge >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Badge_lease_asks_at_once_only_when_the_last_round_is_stale()
    {
        long badgeMs = (long)LobbyPollPlan.Badge.TotalMilliseconds;
        Assert.True(LobbyPollPlan.BadgeWantsRoundNow(1_000, 0));
        Assert.False(LobbyPollPlan.BadgeWantsRoundNow(1_000 + badgeMs - 1, 1_000));
        Assert.True(LobbyPollPlan.BadgeWantsRoundNow(1_000 + badgeMs, 1_000));
    }
}
