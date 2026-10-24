using System;
using System.Collections.Generic;
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
