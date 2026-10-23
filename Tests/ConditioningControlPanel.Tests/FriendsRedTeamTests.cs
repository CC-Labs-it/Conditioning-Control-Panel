using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Friends Everywhere red-team fixes: a second link claim notifies again (#9), Copy link
/// never hands out a link near its end and words the note from the link (#10, #17), the add box
/// claims only our own host, a late `link` reply never revives a retired token, and an accepted
/// request receipt brings the new row at once.</summary>
public class FriendsRedTeamRulesTests
{
    private static readonly DateTimeOffset N = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_second_accepted_claim_by_the_same_account_is_a_new_feed_line()
    {
        var t1 = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var a = FriendsService.FeedLine(new SenderReceipt(null, "request", "u_b", "B", "accepted", t1, null));
        var again = FriendsService.FeedLine(new SenderReceipt(null, "request", "u_b", "B", "accepted", t1, null));
        var later = FriendsService.FeedLine(new SenderReceipt(null, "request", "u_b", "B", "accepted", t1.AddHours(2), null));
        Assert.Equal(a!.Key, again!.Key);
        Assert.NotEqual(a.Key, later!.Key);

        var feed = new FriendsFeed(() => "me");
        Assert.True(feed.Add(a));
        Assert.False(feed.Add(again));
        Assert.True(feed.Add(later));
        Assert.Equal(2, feed.Lines.Count);
    }

    [Theory]
    [InlineData(7 * 24 * 60 - 1, "friends_link_copied_left", 7)]   // minted a moment ago
    [InlineData(60 * 60, "friends_link_copied_left", 2)]          // 2.5 days
    [InlineData(23 * 60 + 30, "friends_link_copied_left", 1)]
    [InlineData(5 * 60 + 10, "friends_link_copied_left_hours", 5)]
    [InlineData(10, "friends_link_copied_left_hours", 1)]
    public void The_copied_note_counts_what_the_link_has_left(int minutesLeft, string key, int left)
    {
        // 60 * 60 minutes = 2.5 days.
        var link = new FriendLink("7K2Q9ABCDEFG", N.AddMinutes(minutesLeft), 3);
        Assert.Equal((key, left, 3), FriendsLinkRules.CopiedNote(link, N));
    }

    [Fact]
    public void A_link_near_its_end_or_spent_is_not_worth_handing_out()
    {
        Assert.False(FriendsLinkRules.WorthHandingOut(new FriendLink("7K2Q9ABCDEFG", N.AddMinutes(2), 20), N));
        Assert.False(FriendsLinkRules.WorthHandingOut(new FriendLink("7K2Q9ABCDEFG", N.AddDays(3), 0), N));
        Assert.False(FriendsLinkRules.WorthHandingOut(null, N));
        Assert.True(FriendsLinkRules.WorthHandingOut(new FriendLink("7K2Q9ABCDEFG", N.AddDays(2), 1), N));
    }

    [Theory]
    [InlineData("https://evil.example/f/7K2Q9ABCDEFG")]
    [InlineData("evil.example/f/7K2Q9ABCDEFG")]
    [InlineData("https://app.cclabs.app.evil.example/f/7K2Q9ABCDEFG")]
    public void A_link_from_another_host_never_becomes_a_claim_in_the_add_box(string pasted)
        => Assert.Equal(("", false), FriendsLinkRules.BoxText(pasted));

    [Fact]
    public void A_full_claim_says_either_list_can_be_the_full_one()
        => Assert.Equal("friends_link_full", FriendsLinkRules.ClaimResultKey(ClaimResult.Full));

    [Fact]
    public void Only_an_accepted_request_receipt_asks_for_the_list()
    {
        var at = DateTime.UtcNow;
        Assert.True(FriendsPollRule.AcceptedRequest(new[] { new SenderReceipt(null, "request", "u_b", null, "accepted", at, null) }));
        Assert.False(FriendsPollRule.AcceptedRequest(new[] { new SenderReceipt("0123456789abcdef", "invite", "u_b", null, "joined", at, null) }));
        Assert.False(FriendsPollRule.AcceptedRequest(Array.Empty<SenderReceipt>()));
        Assert.False(FriendsPollRule.AcceptedRequest(null));
    }
}

public partial class FriendsServiceTests
{
    private static readonly DateTimeOffset R0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_server_link_minutes_from_its_end_is_retired_and_minted_again()
    {
        var api = new FakeApi();
        var svc = new FriendsService(api, () => "u_me", () => true, () => R0, () => false, _ => { });
        api.Links.Enqueue(new FriendLink("7K2Q9ABCDEFG", R0.AddMinutes(2), 3));
        api.Links.Enqueue(new FriendLink("ABCDEFGHJKLM", R0.AddDays(7), 25));
        var link = await svc.GetLinkAsync();
        Assert.Equal("ABCDEFGHJKLM", link!.Token);
        Assert.Equal(1, api.RevokeCalls);
        Assert.Equal(2, api.LinkCalls);

        // Spent (24 of 25 claimed, then the 25th): the same.
        api.Links.Enqueue(new FriendLink("ABCDEFGHJKLM", R0.AddDays(5), 0));
        api.Links.Enqueue(new FriendLink("BCDEFGHJKLMN", R0.AddDays(7), 25));
        Assert.Equal("BCDEFGHJKLMN", (await svc.GetLinkAsync())!.Token);
        Assert.Equal(2, api.RevokeCalls);

        // The revoke fails: nothing near its end is handed out, not even from the cache.
        api.RevokeAnswer = false;
        api.Links.Enqueue(new FriendLink("BCDEFGHJKLMN", R0.AddMinutes(30), 25));
        Assert.Null(await svc.GetLinkAsync());
    }

    [Fact]
    public async Task A_late_link_reply_after_New_link_is_neither_cached_nor_handed_out()
    {
        var api = new FakeApi();
        var svc = new FriendsService(api, () => "u_me", () => true, () => R0, () => false, _ => { });
        var gate = new TaskCompletionSource<FriendLink?>();
        api.LinkGate = gate;
        var slow = svc.GetLinkAsync();

        api.Links.Enqueue(new FriendLink("ABCDEFGHJKLM", R0.AddDays(7), 25));
        Assert.Equal("ABCDEFGHJKLM", (await svc.NewLinkAsync())!.Token);

        gate.SetResult(new FriendLink("7K2Q9ABCDEFG", R0.AddDays(6), 20));
        Assert.Equal("ABCDEFGHJKLM", (await slow)!.Token);
        // The retired token is not the account's own link any more; the fresh one is.
        Assert.Equal(ClaimResult.Self, await svc.ClaimLinkAsync("ABCDEFGHJKLM"));
    }

    [Fact]
    public async Task An_accepted_request_receipt_fetches_the_list_in_the_same_tick()
    {
        var (svc, api, _) = Make();
        await svc.TickAsync(); // poll 0 rides a state anyway
        int before = api.StateCalls;
        api.Polls.Enqueue(new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>(), null,
            new[] { new SenderReceipt(null, "request", "u_b", "B", "accepted", DateTime.UtcNow, null) }));
        await svc.TickAsync();
        Assert.Equal(before + 1, api.StateCalls);
        await svc.TickAsync();
        Assert.Equal(before + 1, api.StateCalls);
    }
}

public partial class FriendsDrawerTests
{
    [Fact]
    public void Copy_link_and_New_link_wait_for_the_call_already_out()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { Fresh = new FriendLink("ABCDEFGHJKLM", Now.AddDays(7), 25) };
            var d = NewDrawer(svc);
            d.CopyText = _ => true;
            svc.LinkGate = new TaskCompletionSource<FriendLink?>();
            var pending = d.CopyLinkAsync();
            Assert.False(pending.IsCompleted);
            Assert.False(d.NewLinkAsync().GetAwaiter().GetResult());
            Assert.False(d.CopyLinkAsync().GetAwaiter().GetResult());
            Assert.Equal(0, svc.NewLinkCalls);
        });
    }
}
