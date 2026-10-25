// ADAPTED from WPF FriendsDrawer.Feed.cs/.Trail.cs/.Blocked.cs at
// 672a29fe169961328aef68affad9646d0de0ebb9. Core owns storage, receipt state and copy rules.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    internal Func<FriendsFeed?> FeedSource { get; set; } = () => FriendsHead.Feed;
    private readonly StackPanel _feedBox = new() { Tag = "friends-feed", IsVisible = false };
    private readonly HashSet<string> _feedFresh = new(StringComparer.Ordinal);
    private FriendsFeed? _feedLive;
    private bool _feedMarking;
    private int _feedExtra;
    private string? _feedTop;

    private void ListenFeed(FriendsFeed? feed)
    {
        var want = _isOpen ? feed : null;
        if (ReferenceEquals(want, _feedLive)) return;
        if (_feedLive != null) _feedLive.Changed -= OnFeedChanged;
        _feedLive = want;
        if (_feedLive != null) _feedLive.Changed += OnFeedChanged;
    }

    private void FoldFeed()
    {
        _feedFresh.Clear();
        _feedExtra = 0;
        ListenFeed(null);
    }

    private void OnFeedChanged()
    {
        if (_feedMarking) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnFeedChanged); return; }
        if (_isOpen) FillFeed(_last);
    }

    private bool IsNew(FeedEntry entry) => !entry.Read || _feedFresh.Contains(entry.Key);

    private void FillFeed(FriendsSnapshot snap)
    {
        var feed = FeedSource();
        ListenFeed(feed);
        var lines = feed?.Lines ?? Array.Empty<FeedEntry>();
        _feedBox.Children.Clear();
        _feedBox.IsVisible = lines.Count > 0;
        if (lines.Count == 0) return;
        int fresh = lines.Count(IsNew);
        // WPF FeedHead: one Grid cell, the count right-aligned over the title.
        var head = new Grid { Margin = new Thickness(8, 10, 8, 4), Tag = "friends-feed-head" };
        head.Children.Add(Label(Loc.Get("friends_feed_title").ToUpperInvariant(), 10, Dim, Mono));
        if (fresh > 0)
        {
            var count = Tagged(Label(Loc.GetF("friends_feed_new", fresh), 10, Pink, Mono), "friends-feed-count");
            count.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(count);
        }
        _feedBox.Children.Add(head);
        int show = Math.Min(lines.Count, FriendsFeedRules.FoldCount(lines.Count, fresh) + _feedExtra);
        var unread = new List<string>();
        for (int i = 0; i < show; i++)
        {
            var entry = lines[i];
            var text = new TextBlock { FontSize = 12, Foreground = IsNew(entry) ? Text : Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Tag = "friends-feed-text" };
            FillFeedLine(text, entry.Event, snap.Friends, IsNew(entry));
            var (key, n) = FriendsFeedRules.Ago(entry.Event.AtUtc, DateTime.UtcNow);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("10,*,Auto"), MinHeight = 22 };
            if (IsNew(entry))
            {
                // ponytail: WPF's static Glow(Pink, 6, 0.9) as a sibling BoxShadow (P82).
                // Keep the ellipse crisp; no Effect under the shared looping drawer FX.
                body.Children.Add(new Border
                {
                    Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Pink,
                    BoxShadow = new BoxShadows(new BoxShadow { Blur = 6, Color = ((ISolidColorBrush)Pink).Color }), Opacity = 0.9,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false, Tag = "friends-feed-new-glow",
                });
                body.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Pink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Tag = "friends-feed-new" });
            }
            Grid.SetColumn(text, 1);
            body.Children.Add(text);
            var ago = Tagged(Label(n is int age ? Loc.GetF(key, age) : Loc.Get(key), 10, Dim, Mono), "friends-feed-ago");
            ago.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(ago, 2);
            body.Children.Add(ago);
            var row = new Border { Child = body, Background = Brushes.Transparent, Margin = new Thickness(4, 0), Padding = new Thickness(4, 0), CornerRadius = new CornerRadius(8), Tag = "friends-feed-line:" + entry.Key };
            ToolTip.SetTip(row, string.Concat(text.Inlines!.OfType<Run>().Select(r => r.Text)));
            if (snap.Friends.Any(f => f.Id == entry.Event.FriendId))
            {
                row.Cursor = Hand();
                AttachSheen(row, false);
                row.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    e.Handled = true;
                    FriendsLanding.SfxClick();
                    OpenFromFeed(entry.Event.FriendId);
                };
            }
            _feedBox.Children.Add(row);
            if (!entry.Read) unread.Add(entry.Key);
            if (i == 0 && _isOpen && _feedTop != null && _feedTop != entry.Key && Amount > 0)
            {
                var slide = new TranslateTransform(-12 * Amount, 0);
                row.RenderTransform = slide;
                Tween(row, 280, p => { row.Opacity = Math.Clamp(p, 0, 1); slide.X = -12 * Amount * (1 - (1 + (1 + 0.4 * Amount) * Math.Pow(p - 1, 3) + 0.4 * Amount * Math.Pow(p - 1, 2))); });
            }
        }
        _feedTop = lines[0].Key;
        int hidden = lines.Count - show;
        if (hidden > 0 || _feedExtra > 0) // WPF adds the foot only when it has a link
        {
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 2) };
            if (hidden > 0)
            {
                int hiddenNew = lines.Skip(show).Count(IsNew);
                var more = Pill(hiddenNew > 0 ? Loc.GetF("friends_feed_more_new", hidden, hiddenNew) : Loc.GetF("friends_feed_more", hidden), Brushes.Transparent, Lilac, "friends-feed-more");
                more.Click += (_, _) => { FriendsLanding.SfxClick(); _feedExtra += FriendsFeedRules.MoreStep; FillFeed(snap); };
                foot.Children.Add(more);
            }
            if (_feedExtra > 0)
            {
                var less = Pill(Loc.Get("friends_feed_less"), Brushes.Transparent, Lilac, "friends-feed-less");
                less.Click += (_, _) => { FriendsLanding.SfxClick(); _feedExtra = 0; FillFeed(snap); };
                foot.Children.Add(less);
            }
            _feedBox.Children.Add(foot);
        }
        if (_isOpen && feed != null && unread.Count > 0)
        {
            foreach (var k in unread) _feedFresh.Add(k);
            _feedMarking = true;
            try { feed.MarkRead(unread); } finally { _feedMarking = false; }
        }
    }

    /// <summary>WPF OpenFromFeed: OpenOn (Toggle, so the card reveal plays), then lay the rebuilt list out
    /// before scrolling to the row (an unmeasured row has no bounds to bring into view).</summary>
    private void OpenFromFeed(string friendId)
    {
        try
        {
            if (_openId != friendId) Toggle(friendId);
            if (_rows.TryGetValue(friendId, out var row)) { _list.UpdateLayout(); row.BringIntoView(); }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed open failed: {E}", ex.Message); }
    }

    internal static void FillFeedLine(TextBlock text, FriendEvent e, IReadOnlyList<Friend> friends, bool isNew)
    {
        var line = FriendsFeedRules.Describe(e);
        var name = FriendsFeedRules.NameFor(e, friends) ?? Loc.Get("friends_feed_someone");
        var detail = line.Detail == null ? "" : line.DetailIsKey ? Loc.Get(line.Detail) : line.Detail;
        // Replace the template before inserting names/titles: literal {0} in a title stays literal.
        var parts = Loc.Get(line.Key).Split("{0}");
        for (int i = 0; i < parts.Length; i++)
        {
            text.Inlines!.Add(new Run(parts[i].Replace("{1}", detail)));
            if (i < parts.Length - 1) text.Inlines.Add(new Run(name) { FontFamily = Display, FontWeight = FontWeight.SemiBold, Foreground = isNew ? Text : Lilac });
        }
    }

    private void OnTrailsChanged() { if (_isOpen) Render(); }

    private TextBlock? TrailLine(Friend f)
    {
        if (_results.ContainsKey(f.Id) || _svc?.LastSentTo(f.Id) is not { } trail) return null;
        var (kindKey, stateKey) = FriendsDrawerRules.TrailKeys(trail);
        var tone = FriendsDrawerRules.ToneOf(trail) switch
        {
            FriendsDrawerRules.TrailTone.Seen => Lilac, FriendsDrawerRules.TrailTone.Yes => Mint,
            FriendsDrawerRules.TrailTone.No => Gold, FriendsDrawerRules.TrailTone.Quiet => Dim, _ => Muted,
        };
        var line = Label("", 10.5, Muted);
        line.Margin = new Thickness(0, 1, 0, 0);
        line.Tag = "friends-trail:" + trail.State;
        line.Inlines!.Add(new Run(Loc.Get(kindKey) + "  "));
        for (int i = 0; i < trail.Steps; i++) line.Inlines.Add(new Run(i < trail.Lit ? "●" : "○") { FontSize = 8, Foreground = i < trail.Lit ? tone : Dim });
        line.Inlines.Add(new Run("  " + Loc.Get(stateKey)) { Foreground = tone });
        ToolTip.SetTip(line, Loc.Get(trail.Kind == SendKind.Invite ? "friends_trail_tip_invite" : "friends_trail_tip"));
        return line;
    }

    private static IReadOnlyList<BlockedFriend> BlockedRows(FriendsSnapshot snap) => snap.Blocked
        ?? FriendsLocalBlocks.Shared.For(FriendsLocalBlocks.Account()).Select(b => new BlockedFriend(b.Id, b.Name)).ToArray();

    private void RememberBlock(string id, string name)
    {
        if (_last.Blocked == null) FriendsLocalBlocks.Shared.Add(FriendsLocalBlocks.Account(), id, name, DateTimeOffset.UtcNow);
    }
}
