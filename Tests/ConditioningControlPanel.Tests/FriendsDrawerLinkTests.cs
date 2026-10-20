using System;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Friend links in the drawer (PR D1b): Copy link beside the code, New link in the add box,
/// a pasted link claims. Every link key is worded.</summary>
public partial class FriendsDrawerTests
{
    [Fact]
    public void Copy_link_copies_the_link_or_says_it_copied_the_code()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { Link = new FriendLink("7K2Q9ABCDEFG", Now.AddDays(7), 25) };
            var d = NewDrawer(svc);
            string? copied = null;
            d.CopyText = t => { copied = t; return true; };
            d.LinkClock = () => Now;
            Assert.NotNull(Find(d, "friends-copy-link"));

            Assert.True(d.CopyLinkAsync().GetAwaiter().GetResult());
            Assert.Equal("https://app.cclabs.app/f/7K2Q9ABCDEFG", copied);
            Assert.Equal(Loc.GetF("friends_link_copied_left", 7, 25), d.LinkNoteText);

            // The note reads what the link has left, never a fixed week (red-team #17).
            svc.Link = new FriendLink("7K2Q9ABCDEFG", Now.AddHours(5).AddMinutes(10), 3);
            Assert.True(d.CopyLinkAsync().GetAwaiter().GetResult());
            Assert.Equal(Loc.GetF("friends_link_copied_left_hours", 5, 3), d.LinkNoteText);
            Assert.Contains("3", d.LinkNoteText);

            svc.Link = null;
            Assert.False(d.CopyLinkAsync().GetAwaiter().GetResult());
            Assert.Equal("CCP-7K2Q9", copied);
            Assert.Equal(Loc.Get("friends_link_offline"), d.LinkNoteText);
            Assert.NotNull(Find(d, "friends-link-note"));
        });
    }

    [Fact]
    public void A_pasted_link_in_the_add_box_claims_it()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextClaim = ClaimResult.Unusable };
            var d = NewDrawer(svc);
            var r = d.AddByCodeAsync("https://app.cclabs.app/f/7k2q9abcdefg").GetAwaiter().GetResult();
            Assert.Equal(AddResult.NotFound, r);
            Assert.Equal("7K2Q9ABCDEFG", svc.Claims.Single());
            Assert.Empty(svc.Adds);
            Assert.Equal(Loc.Get("friends_link_unusable"), d.AddResultText);
        });
    }

    [Fact]
    public void New_link_sits_in_the_add_box_and_copies_the_fresh_link()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { Fresh = new FriendLink("ABCDEFGHJKLM", Now.AddDays(7), 25) };
            var d = NewDrawer(svc);
            string? copied = null;
            d.CopyText = t => { copied = t; return true; };
            Assert.NotNull(Find(d, "friends-new-link-line"));
            Assert.True(d.NewLinkAsync().GetAwaiter().GetResult());
            Assert.Equal("https://app.cclabs.app/f/ABCDEFGHJKLM", copied);
            Assert.Equal(Loc.Get("friends_link_new_done"), d.AddResultText);
        });
    }

    [Fact]
    public void Every_link_key_is_in_english()
    {
        var keys = new[] { "friends_link_copy", "friends_link_copy_tip", "friends_link_copied_left",
            "friends_link_copied_left_hours", "friends_link_full", "friends_link_offline",
            "friends_link_new_line", "friends_link_new", "friends_link_new_done", "friends_link_self", "friends_link_unusable" }
            .Concat(Enum.GetValues<ClaimResult>().Select(FriendsLinkRules.ClaimResultKey));
        foreach (var k in keys) Assert.NotEqual(k, Loc.Get(k));
    }
}
