using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "someone used your invite code" notice: told once per code, a subscription told once per
/// step of the lifetime count, and nothing invented on the first read of an account.
/// </summary>
public class InviteClaimNoticeTests
{
    private const string Uid = "u_abc12345";

    private static InviteMine Mine(int total, params InviteSlot[] slots)
        => new(true, new InviteSnapshot(slots, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), total), total);

    private static InviteSlot Slot(string code, InviteSlotState state, string? name = null)
        => new(code, state, name, state == InviteSlotState.Open ? null : 2);

    [Fact]
    public void AnOpenCode_IsNoNews()
    {
        var stamps = new Dictionary<string, string>();
        var news = InviteClaimNotice.Decide(Mine(0, Slot("AAAA-BBBB-CC", InviteSlotState.Open)), Uid, stamps, out _);
        Assert.Empty(news);
    }

    [Fact]
    public void AClaimedCode_IsToldOnce_WithTheFriendsName()
    {
        var stamps = new Dictionary<string, string>();
        var mine = Mine(0, Slot("AAAA-BBBB-CC", InviteSlotState.Trying, " ANN "));

        var first = InviteClaimNotice.Decide(mine, Uid, stamps, out bool changed);
        var again = InviteClaimNotice.Decide(mine, Uid, stamps, out bool changedAgain);

        var item = Assert.Single(first);
        Assert.Equal(InviteNewsKind.Claimed, item.Kind);
        Assert.Equal("ANN", item.Name);
        Assert.True(changed);
        Assert.Empty(again);
        Assert.False(changedAgain);
    }

    [Fact]
    public void AClaimWithNoName_StillTells()
    {
        var stamps = new Dictionary<string, string>();
        var item = Assert.Single(InviteClaimNotice.Decide(Mine(0, Slot("AAAA-BBBB-CC", InviteSlotState.Trying)), Uid, stamps, out _));
        Assert.Null(item.Name);
    }

    [Fact]
    public void TheFirstReadOfAnAccount_OnlyRemembersTheCount()
    {
        var stamps = new Dictionary<string, string>();
        var news = InviteClaimNotice.Decide(Mine(3, Slot("AAAA-BBBB-CC", InviteSlotState.Open)), Uid, stamps, out bool changed);
        Assert.Empty(news);
        Assert.True(changed);
    }

    [Fact]
    public void ARisingCount_IsASubscription_ToldOnce()
    {
        var stamps = new Dictionary<string, string>();
        InviteClaimNotice.Decide(Mine(1, Slot("AAAA-BBBB-CC", InviteSlotState.Trying, "ANN")), Uid, stamps, out _);

        var mine = Mine(2, Slot("AAAA-BBBB-CC", InviteSlotState.Converted, "ANN"));
        var item = Assert.Single(InviteClaimNotice.Decide(mine, Uid, stamps, out _));
        Assert.Equal(InviteNewsKind.Subscribed, item.Kind);
        Assert.Equal("ANN", item.Name);
        Assert.Empty(InviteClaimNotice.Decide(mine, Uid, stamps, out _));
    }

    [Fact]
    public void ASubscriptionFromAnEarlierMonth_HasNoName()
    {
        var stamps = new Dictionary<string, string>();
        InviteClaimNotice.Decide(Mine(0, Slot("AAAA-BBBB-CC", InviteSlotState.Open)), Uid, stamps, out _);
        var item = Assert.Single(InviteClaimNotice.Decide(Mine(1, Slot("AAAA-BBBB-CC", InviteSlotState.Open)), Uid, stamps, out _));
        Assert.Equal(InviteNewsKind.Subscribed, item.Kind);
        Assert.Null(item.Name);
    }

    [Fact]
    public void ANewMonth_ForgetsOldCodes_AndKeepsTheCount()
    {
        var stamps = new Dictionary<string, string>();
        InviteClaimNotice.Decide(Mine(1, Slot("AAAA-BBBB-CC", InviteSlotState.Trying, "ANN")), Uid, stamps, out _);
        InviteClaimNotice.Decide(Mine(1, Slot("DDDD-EEEE-FF", InviteSlotState.Open)), Uid, stamps, out _);

        Assert.DoesNotContain(stamps.Keys, k => k.Contains("AAAA-BBBB-CC"));
        Assert.Equal("1", stamps["invite:total:" + Uid]);
    }

    [Fact]
    public void ARefusalOrAnOfflineRead_TouchesNothing()
    {
        var stamps = new Dictionary<string, string>();
        InviteClaimNotice.Decide(Mine(1, Slot("AAAA-BBBB-CC", InviteSlotState.Trying, "ANN")), Uid, stamps, out _);
        var before = new Dictionary<string, string>(stamps);

        Assert.Empty(InviteClaimNotice.Decide(InviteMine.Unreachable, Uid, stamps, out bool offline));
        Assert.Empty(InviteClaimNotice.Decide(Mine(1, Slot("AAAA-BBBB-CC", InviteSlotState.Trying, "ANN")), null, stamps, out bool signedOut));

        Assert.False(offline);
        Assert.False(signedOut);
        Assert.Equal(before, stamps);
    }

    [Fact]
    public void AnotherAccountOnThisPc_KeepsItsOwnCount()
    {
        var stamps = new Dictionary<string, string>();
        InviteClaimNotice.Decide(Mine(0, Slot("AAAA-BBBB-CC", InviteSlotState.Open)), Uid, stamps, out _);
        var news = InviteClaimNotice.Decide(Mine(5, Slot("GGGG-HHHH-JJ", InviteSlotState.Open)), "u_other0001", stamps, out _);
        Assert.Empty(news);
    }
}
