using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "What happened" in the drawer and the unread badge on the chip, against an in-memory feed:
/// newest first, folded to two recent lines, read once the drawer is open (the dot stays until
/// the drawer folds), the open list in pages, the x, and no section at all while nothing
/// happened. Shares the drawer suite's fake and helpers.
/// </summary>
public partial class FriendsDrawerTests
{
    private static readonly DateTime FeedT0 = DateTime.UtcNow.AddHours(-1);

    private static FriendsFeed FeedWith(int count, bool read = false)
    {
        var feed = new FriendsFeed(() => "me");
        for (var i = 0; i < count; i++)
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", FeedT0.AddMinutes(i), "k" + i, null, "hi"));
        if (read) feed.MarkAllRead();
        return feed;
    }

    /// <summary>Runs <paramref name="body"/> with the drawer drawing <paramref name="feed"/>.</summary>
    private static void WithFeed(FriendsFeed? feed, Action body)
    {
        var source = FriendsDrawer.FeedSource;
        try
        {
            FriendsDrawer.FeedSource = () => feed;
            body();
        }
        finally { FriendsDrawer.FeedSource = source; }
    }

    [Fact]
    public void Feed_folds_to_two_recent_lines_and_opening_the_drawer_reads_them_all()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = FeedWith(5);
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                // Built but folded: two lines drawn, nothing read.
                Assert.False(d.FeedOpen);
                Assert.Equal(new[] { "k4", "k3" }, d.FeedLineKeys);
                Assert.Equal(5, feed.Unread);
                // The feed has no place in the friend sections.
                Assert.Equal(new[] { "friends_section_online", "friends_section_requests", "friends_section_offline" }, d.SectionKeys);

                // An open drawer reads every line; the two shown keep their dots, "3 more, 3 new" under them.
                d.IsOpen = true;
                d.Render();
                Assert.Equal(0, feed.Unread);
                Assert.Equal(2, Tags(d.FeedSection).Count(t => t == "friends-feed-new"));
                Assert.NotNull(Find(d.FeedSection, "friends-feed-count"));
                var more = (Button)Find(d.FeedSection, "friends-feed-more")!;
                Assert.Equal(Loc.GetF("friends_feed_more_new", 3, 3), more.Content);

                // "more" opens the section: every line, newest first, dots on the new ones.
                d.ToggleFeed();
                Assert.True(d.FeedOpen);
                Assert.Equal(new[] { "k4", "k3", "k2", "k1", "k0" }, d.FeedLineKeys);
                Assert.Equal(5, Tags(d.FeedSection).Count(t => t == "friends-feed-new"));
                Assert.Null(Find(d.FeedSection, "friends-feed-more"));

                // Folded again and the drawer reopened: read, no dots, two lines and "3 more".
                d.ToggleFeed();
                d.FeedFolded();
                d.Render();
                Assert.Equal(new[] { "k4", "k3" }, d.FeedLineKeys);
                Assert.DoesNotContain("friends-feed-new", Tags(d.FeedSection));
                Assert.Null(Find(d.FeedSection, "friends-feed-count"));
                Assert.NotNull(Find(d.FeedSection, "friends-feed-total"));
                more = (Button)Find(d.FeedSection, "friends-feed-more")!;
                Assert.Equal(Loc.GetF("friends_feed_more", 3), more.Content);
                Layout(d);
            });
        });
    }

    [Fact]
    public void The_open_feed_pages_ten_at_a_time_and_less_goes_back()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = FeedWith(25);
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                FriendsFold.Update(s => s.FeedOpen = true);
                d.IsOpen = true;
                d.Render();
                Assert.Equal(FriendsFeedRules.MoreStep, d.FeedLineKeys.Count);
                Assert.Equal(0, feed.Unread);
                var more = (Button)Find(d.FeedSection, "friends-feed-more")!;
                Assert.Equal(Loc.GetF("friends_feed_more_new", 15, 15), more.Content);

                d.ShowMoreFeed();
                Assert.Equal(20, d.FeedLineKeys.Count);
                Assert.NotNull(Find(d.FeedSection, "friends-feed-less"));
                d.ShowMoreFeed();
                Assert.Equal(25, d.FeedLineKeys.Count);
                Assert.Null(Find(d.FeedSection, "friends-feed-more"));

                d.ShowLessFeed();
                Assert.Equal(FriendsFeedRules.MoreStep, d.FeedLineKeys.Count);
            });
        });
    }

    [Fact]
    public void Old_read_lines_leave_the_preview_and_the_x_hides_the_rest()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var now = DateTime.UtcNow;
            var feed = new FriendsFeed(() => "me");
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", now.AddDays(-3), "old1", null, "hi"));
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", now.AddDays(-2), "old2", null, "hi"));
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", now.AddHours(-2), "recent", null, "hi"));
            feed.MarkAllRead();
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                // Read and older than a day: out of the preview, still in the count.
                Assert.Equal(new[] { "recent" }, d.FeedLineKeys);
                Assert.Equal(Loc.GetF("friends_feed_more", 2), ((Button)Find(d.FeedSection, "friends-feed-more")!).Content);

                // An old line nobody has seen yet still shows.
                feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "kit", "Kit", now.AddHours(-30), "late", null, "hi"));
                d.Render();
                Assert.Equal(new[] { "recent", "late" }, d.FeedLineKeys);

                // The x empties the preview; the section stays, and opening it shows everything.
                Assert.NotNull(Find(d.FeedSection, "friends-feed-clear"));
                d.ClearFeedPreview(animate: false);
                Assert.Empty(d.FeedLineKeys);
                Assert.Equal(Visibility.Visible, d.FeedSection.Visibility);
                Assert.Null(Find(d.FeedSection, "friends-feed-clear"));
                d.ToggleFeed();
                Assert.Equal(new[] { "recent", "late", "old2", "old1" }, d.FeedLineKeys);

                // Folded again, a line landing after the clear shows.
                d.ToggleFeed();
                feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "kit", "Kit", now, "fresh", null, "hi"));
                d.Render();
                Assert.Equal(new[] { "fresh" }, d.FeedLineKeys);
            });
        });
    }

    [Fact]
    public void Feed_preview_rule_keeps_two_new_or_recent_lines_after_the_clear()
    {
        var now = DateTime.UtcNow;
        FeedEntry E(string k, double hoursAgo, bool read) =>
            new(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", now.AddHours(-hoursAgo), k, null, "hi"), read);
        var lines = new[] { E("a", 1, true), E("b", 2, false), E("c", 3, true), E("d", 30, false), E("e", 40, true) };
        Assert.Equal(new[] { "a", "b" }, FriendsFold.FeedPreviewLines(lines, l => !l.Read, now, null).Select(l => l.Key));
        // An unread line stays however old; a read one leaves after a day.
        Assert.Equal(new[] { "c", "d" }, FriendsFold.FeedPreviewLines(lines.Skip(2).ToArray(), l => !l.Read, now, null).Select(l => l.Key));
        // Nothing at or before the clear mark.
        Assert.Equal(new[] { "a" }, FriendsFold.FeedPreviewLines(lines, l => !l.Read, now, now.AddHours(-1.5)).Select(l => l.Key));
        Assert.Empty(FriendsFold.FeedPreviewLines(lines, l => !l.Read, now, now));
    }

    [Fact]
    public void A_line_landing_in_the_open_drawer_shows_at_once_and_is_read()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = FeedWith(2, read: true);
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                d.IsOpen = true;
                d.Render();
                Assert.DoesNotContain("friends-feed-new", Tags(d.FeedSection));

                feed.Add(new FriendEvent(FriendEventKind.InviteUnanswered, "kit", "Kit", DateTime.UtcNow, "rc:x:expired"));
                Assert.Equal("rc:x:expired", d.FeedLineKeys.First());
                Assert.Equal(0, feed.Unread);
                Assert.Single(Tags(d.FeedSection), t => t == "friends-feed-new");

                // A folded drawer does not follow the feed and reads nothing.
                d.FeedFolded();
                d.IsOpen = false;
                feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "dee", "Dee", DateTime.UtcNow, "req-in:dee:1"));
                Assert.Equal(1, feed.Unread);
                Assert.Equal("rc:x:expired", d.FeedLineKeys.First());
            });
        });
    }

    [Fact]
    public void A_line_about_a_friend_opens_their_card()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = new FriendsFeed(() => "me");
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", FeedT0, "p1", null, "hi"));
            feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "dee", "Dee", FeedT0.AddMinutes(1), "r1"));
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                var samLine = (Border)Find(d.FeedSection, "friends-feed-line:p1")!;
                var deeLine = (Border)Find(d.FeedSection, "friends-feed-line:r1")!;
                Assert.Equal(System.Windows.Input.Cursors.Hand, samLine.Cursor);
                Assert.Null(deeLine.Cursor);   // not on the list (a request): nothing to open

                d.OpenFromFeed("sam");
                Assert.Equal("sam", d.OpenFriendId);
                Assert.NotNull(Find(d.RowFor("sam")!, "friends-card"));
            });
        });
    }

    [Fact]
    public void An_empty_or_missing_feed_draws_no_section()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            WithFeed(new FriendsFeed(() => "me"), () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                Assert.Equal(Visibility.Collapsed, d.FeedSection.Visibility);
                Assert.Empty(d.FeedLineKeys);
            });
            WithFeed(null, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                Assert.Equal(Visibility.Collapsed, d.FeedSection.Visibility);
                Assert.Equal(new[] { "kit", "sam", "in:dee", "out:ash", "robin", "noor" }, d.RowIds);
            });
        });
    }

    [Fact]
    public void A_feed_line_reads_as_a_sentence_with_the_name()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var friends = Sample().Friends;
            string Say(FriendEvent e) => FriendsDrawer.FillLine(new TextBlock(), e, friends, true);

            var poke = Say(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", FeedT0, "a", null, "tut"));
            Assert.Equal(Loc.Get("friends_feed_poke").Replace("{0}", "Sam").Replace("{1}", Loc.Get("friends_poke_tut")), poke);

            // No name on the line: the friend list's name, else "someone".
            var seen = Say(new FriendEvent(FriendEventKind.ItemSeen, "noor", null, FeedT0, "b", null, ReceiptKind.Invite));
            Assert.Equal(Loc.Get("friends_feed_seen_invite").Replace("{0}", "Noor"), seen);
            var ghost = Say(new FriendEvent(FriendEventKind.InviteUnanswered, "ghost", null, FeedT0, "c"));
            Assert.Equal(Loc.Get("friends_feed_no_answer").Replace("{0}", Loc.Get("friends_feed_someone")), ghost);

            // A title holding a placeholder stays a title.
            var watch = Say(new FriendEvent(FriendEventKind.WatchReceived, "sam", "Sam", FeedT0, "d", null, "{0} tricks"));
            Assert.Equal(Loc.Get("friends_feed_watch").Replace("{0}", "Sam").Replace("{1}", "{0} tricks"), watch);
        });
    }

    [Fact]
    public void Chip_badge_counts_unread_feed_lines()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            PresenceAsk.Asked = () => true;
            var feed = FeedWith(3);
            var svc = new FakeFriends(Sample());
            var chip = new FriendsRailChip(svc, feed);
            chip.Rebind();
            Assert.Equal(3, chip.UnreadBadge);
            Assert.Equal(2, chip.PillCount);
            Assert.Contains(Loc.GetF("friends_feed_new", 3), chip.ToolTip as string);

            feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "dee", "Dee", DateTime.UtcNow, "req-in:dee:1"));
            Assert.Equal(4, chip.UnreadBadge);

            for (var i = 0; i < 8; i++)
                feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "kit", "Kit", DateTime.UtcNow, "more" + i, null, "hi"));
            Assert.Equal(10, chip.UnreadBadge);   // "9+"

            feed.MarkAllRead();
            Assert.Equal(0, chip.UnreadBadge);
            Assert.EndsWith(Loc.Get("friends_chip_tooltip"), chip.ToolTip as string); // the full name may lead

            // Signed out: no badge, whatever the feed holds.
            feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "ash", "Ash", DateTime.UtcNow, "req-in:ash:1"));
            Assert.Equal(1, chip.UnreadBadge);
            svc.IsAvailable = false;
            svc.Push(Sample());
            Assert.Equal(0, chip.UnreadBadge);
        });
    }
}
