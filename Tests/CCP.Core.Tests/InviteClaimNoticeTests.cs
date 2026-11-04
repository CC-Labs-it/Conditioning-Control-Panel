using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace CCP.Core.Tests;

public class InviteClaimNoticeTests
{
    private static InviteMine Read(int total, params InviteSlot[] slots) => new(true, new InviteSnapshot(slots, null, total));

    [Fact]
    public void ClaimsAndSubscriptionsAreStampedAcrossReadsAndAccounts()
    {
        var stamps = new Dictionary<string, string>();
        var claimed = new InviteSlot("code", InviteSlotState.Trying, " Friend ", null);
        var first = InviteClaimNotice.Decide(Read(4, claimed), "a", stamps, out var changed);
        Assert.True(changed);
        Assert.Equal("Friend", Assert.Single(first).Name);
        Assert.Equal(InviteNewsKind.Claimed, first[0].Kind);
        Assert.Empty(InviteClaimNotice.Decide(Read(4, claimed), "a", stamps, out changed));
        Assert.False(changed);
        var persisted = new Dictionary<string, string>(stamps);
        var converted = claimed with { State = InviteSlotState.Converted };
        var subscription = Assert.Single(InviteClaimNotice.Decide(Read(5, converted), "a", persisted, out changed));
        Assert.True(changed);
        Assert.Equal(InviteNewsKind.Subscribed, subscription.Kind);
        Assert.Equal("Friend", subscription.Name);
        Assert.Empty(InviteClaimNotice.Decide(Read(5, converted), "a", persisted, out changed));
        Assert.False(changed);
        Assert.Empty(InviteClaimNotice.Decide(Read(9, converted), "b", persisted, out changed));
        Assert.True(changed);
        Assert.Equal(InviteNewsKind.Subscribed, Assert.Single(InviteClaimNotice.Decide(Read(10, converted), "b", persisted, out changed)).Kind);
        var before = persisted.ToArray();
        Assert.Empty(InviteClaimNotice.Decide(InviteMine.Unreachable, "b", persisted, out changed));
        Assert.False(changed);
        Assert.Equal(before, persisted.ToArray());
    }
}
