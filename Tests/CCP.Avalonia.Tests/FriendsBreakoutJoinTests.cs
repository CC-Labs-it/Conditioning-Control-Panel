using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsBreakoutJoinTests
{
    private static InboxItem Invite => new("breakout-invite", SendKind.Invite, "u_friend", "Friend",
        null, null, InviteDestination.Breakout, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

    private static InboxItem Fresh(string dest) => new(Guid.NewGuid().ToString("N")[..16], SendKind.Invite, "u_friend", "Friend",
        null, null, dest, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

    /// <summary>The card's answers, as KnockCard reports them: Answer later files the Inbox row, Join (Go) takes it
    /// back, reports Joined and opens the destination (WPF OnKnockDone :487, Join :522).</summary>
    private static Task Answer(InboxItem item, Action assert) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = FriendsDrawerTests.Service();
        var (service, anchor) = (FriendsLanding.Service, FriendsLanding.Anchor);
        try
        {
            FriendsLanding.Service = () => svc;
            FriendsLanding.Anchor = () => null;
            FriendsLanding.OnKnockDone(item, KnockOutcome.Later);
            Assert.True(StartupLadder.Inbox.Contains("friends:" + item.Id));
            FriendsLanding.OnKnockDone(item, KnockOutcome.Go);
            Assert.False(StartupLadder.Inbox.Contains("friends:" + item.Id));
            // Already reported Joined: the shared memory refuses a second report.
            Assert.False(FriendsSeen.Shared.Report(svc, ReceiptReport.Item(item.Id, ReceiptState.Joined)));
            assert();
        }
        finally
        {
            (FriendsLanding.Service, FriendsLanding.Anchor) = (service, anchor);
            StartupLadder.Inbox.Remove("friends:" + item.Id);
        }
        await Task.CompletedTask;
    });

    [Fact]
    public async Task ARaceKnockNamesRacingThoughtsAndJoinOpensTheWebRace()
    {
        var item = Fresh(InviteDestination.Race);
        Assert.Equal(string.Format(Loc.Get("friends_land_invite_line"), Loc.Get("friends_land_dest_race")),
            FriendsLanding.KnockLine(item));
        var open = FriendsLanding.OpenLink;
        var opened = new List<string>();
        FriendsLanding.OpenLink = u => { opened.Add(u); return true; };
        try { await Answer(item, () => Assert.Equal(InviteDestination.RaceWebUrl, Assert.Single(opened))); }
        finally { FriendsLanding.OpenLink = open; }
    }

    [Fact]
    public async Task AFreeAccountsJoinOnTheCardOpensTheBreakoutDemo()
    {
        var (lab, launch) = (CoreEntitlement.HasLabProvider, FriendsLanding.LaunchGame);
        var opened = new List<string>();
        CoreEntitlement.HasLabProvider = () => false;
        FriendsLanding.LaunchGame = opened.Add;
        try { await Answer(Fresh(InviteDestination.Breakout), () => Assert.Equal("breakoutdemo", Assert.Single(opened))); }
        finally { (CoreEntitlement.HasLabProvider, FriendsLanding.LaunchGame) = (lab, launch); }
    }

    [Fact]
    public void BreakoutKnockNamesTheStation()
        => Assert.Equal(string.Format(Loc.Get("friends_land_invite_line"), Loc.Get("friends_land_dest_breakout")),
            FriendsLanding.KnockLine(Invite));

    [Fact]
    public void FreeAccountJoinsTheExistingDemoHost()
    {
        var lab = CoreEntitlement.HasLabProvider;
        var launch = FriendsLanding.LaunchGame;
        var opened = new List<string>();
        try
        {
            CoreEntitlement.HasLabProvider = () => false;
            FriendsLanding.LaunchGame = opened.Add;
            Assert.True(FriendsLanding.CanJoin(Invite));
            FriendsLanding.Join(Invite);
            Assert.Equal("breakoutdemo", Assert.Single(opened));
            Assert.Equal("desktop=1&demo=1", GameWindow.Games[opened[0]].Query);
            Assert.False(InviteDestination.IsSendable(InviteDestination.Breakout));
        }
        finally { CoreEntitlement.HasLabProvider = lab; FriendsLanding.LaunchGame = launch; }
    }

    [Fact]
    public void EntitledAccountDoesNotJoinADemoInPlaceOfTheMissingFullHost()
    {
        var lab = CoreEntitlement.HasLabProvider;
        var launch = FriendsLanding.LaunchGame;
        var opened = new List<string>();
        try
        {
            CoreEntitlement.HasLabProvider = () => true;
            FriendsLanding.LaunchGame = opened.Add;
            Assert.False(GameWindow.Games.ContainsKey("breakout"));
            Assert.False(FriendsLanding.CanJoin(Invite));
            FriendsLanding.Join(Invite);
            Assert.Empty(opened);
        }
        finally { CoreEntitlement.HasLabProvider = lab; FriendsLanding.LaunchGame = launch; }
    }
}
