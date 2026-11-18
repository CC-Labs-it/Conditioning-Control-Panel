using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>What the Lobby costs the server: the rail badge polls slowly on its own, a full
/// watcher restores the 10 s pace, and a tier refusal on the race list is not re-asked every
/// round (Nov 2026: the badge lease kept four calls every 10 s going for every open panel,
/// and two in three race-list calls were the same 403).</summary>
public class LobbyPaceTests
{
    private static LobbyService Service(Func<RaceOpenReply> race, Func<long> now, Action? onRace = null) => new()
    {
        SignedIn = () => true,
        FetchChess = _ => Task.FromResult(PbpLobbyReply.Empty),
        FetchGoon = () => Task.FromResult(OpenTablesReply.Empty),
        FetchRemote = () => Task.FromResult(((System.Collections.Generic.IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)),
        FetchRace = _ => { onRace?.Invoke(); return Task.FromResult(race()); },
        ReadFriends = () => Array.Empty<ConditioningControlPanel.Services.Friends.Friend>(),
        NowMs = now,
    };

    private static RaceOpenReply TierLocked() => new(Array.Empty<RaceOpenRoom>(), true, "tier");

    [Fact]
    public void Badge_only_waits_the_badge_pace_whatever_the_answer()
    {
        Assert.Equal(LobbyPollPlan.Badge, LobbyPollPlan.Next(LobbyPollPlan.Base, ok: true, badgeOnly: true));
        Assert.Equal(LobbyPollPlan.Badge, LobbyPollPlan.Next(LobbyPollPlan.Max, ok: false, badgeOnly: true));
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

    [Fact]
    public async Task A_tier_refusal_on_the_race_list_is_held_not_reasked()
    {
        long now = 1_000_000;
        int calls = 0;
        using var lobby = Service(TierLocked, () => now, () => calls++);

        await lobby.RefreshAsync();
        await lobby.RefreshAsync();
        Assert.Equal(1, calls);

        now += (long)LobbyPollPlan.TierLockHold.TotalMilliseconds;
        await lobby.RefreshAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_open_race_list_is_asked_every_round()
    {
        long now = 1_000_000;
        int calls = 0;
        using var lobby = Service(() => new RaceOpenReply(Array.Empty<RaceOpenRoom>(), true), () => now, () => calls++);
        await lobby.RefreshAsync();
        await lobby.RefreshAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Opening_the_lobby_asks_the_race_list_again()
    {
        long now = 1_000_000;
        int calls = 0;
        using var lobby = Service(TierLocked, () => now, () => calls++);
        await lobby.RefreshAsync();
        Assert.Equal(1, calls);

        using var lease = lobby.Watch();
        await lobby.RefreshAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Badge_alone_is_slow_and_a_full_watcher_restores_the_pace()
    {
        long now = 1_000_000;
        using var lobby = Service(() => new RaceOpenReply(Array.Empty<RaceOpenRoom>(), true), () => now);

        var badge = lobby.WatchBadge();
        await lobby.RefreshAsync();
        Assert.True(lobby.IsWatched);
        Assert.True(lobby.BadgeOnly);
        Assert.Equal(LobbyPollPlan.Badge, lobby.CurrentInterval);

        var page = lobby.Watch();
        await lobby.RefreshAsync();
        Assert.False(lobby.BadgeOnly);
        Assert.Equal(LobbyPollPlan.Base, lobby.CurrentInterval);

        page.Dispose();
        Assert.True(lobby.BadgeOnly);
        Assert.Equal(LobbyPollPlan.Badge, lobby.CurrentInterval);

        badge.Dispose();
        Assert.False(lobby.IsWatched);
    }
}
