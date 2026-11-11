using System.Linq;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class GlassroadInviteRulesTests
{
    private static Friend Friend(string id, string name, bool online) =>
        new(id, name, null, 0, online, FriendPresence.None, false);

    [Fact]
    public void ListContainsOnlyNamedOnlineFriendsAndStopsAtFifty()
    {
        var friends = new[] { Friend("offline", "Off", false), Friend("", "No id", true), Friend("blank", " ", true) }
            .Concat(Enumerable.Range(0, 55).Select(i => Friend("u" + i, "Name " + i, true)));
        var rows = GlassroadInviteRules.FriendRows(friends);
        Assert.Equal(50, rows.Count);
        Assert.Equal(new GlassroadInviteRules.FriendRow("u0", "Name 0"), rows[0]);
        Assert.Equal("u49", rows[49].uid);
        Assert.Empty(GlassroadInviteRules.FriendRows(null));
    }

    [Fact]
    public void InviteChecksFriendAndRoomAndWordsKnownRefusals()
    {
        var friends = new[] { Friend("u1", "One", true) };
        Assert.True(GlassroadInviteRules.CanInvite(friends, "u1", "abc123def456"));
        Assert.False(GlassroadInviteRules.CanInvite(friends, "u2", "abc123def456"));
        Assert.False(GlassroadInviteRules.CanInvite(friends, "u1", "ABC123def456"));
        var refused = new SendOutcome(SendResult.Refused, null) { Reason = "needs_prime" };
        Assert.Equal("friends_race_needs_prime", GlassroadInviteRules.NoteKey(refused));
        Assert.False(GlassroadInviteRules.IsGood(refused));
        Assert.Equal("friends_result_refused", GlassroadInviteRules.NoteKey(refused with { Reason = "other" }));
        Assert.True(GlassroadInviteRules.IsGood(new SendOutcome(SendResult.Squelched, null)));
        Assert.Equal("Try again", GlassroadInviteRules.Sentence("try again"));
    }
}
