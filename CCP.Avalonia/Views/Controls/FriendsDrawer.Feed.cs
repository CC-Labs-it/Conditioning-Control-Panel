// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Feed.cs and .Trail.cs: the
// "WHAT HAPPENED" feed at the top of the list (Core FriendsFeed + FriendsFeedRules) and, under a
// friend's name, the trail of the latest poke / invite / watch you sent them (Core SentTrail +
// FriendsDrawerRules.TrailKeys/ToneOf).
// ponytail: no slide-in for a line that lands while open (WPF LandLine, part of the Juice motion
// this head does not have yet) and no glow on the pink dot (P82: no Effect in rows).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>The feed this drawer draws: the app's own (WPF App.FriendsFeed). The chip's badge reads it too.</summary>
    internal Func<FriendsFeed?> FeedSource { get; set; } = () => FriendsHead.Feed;

    /// <summary>WPF FriendsLook.Body and HoverBrush (white at 0x14).</summary>
    internal static readonly FontFamily Body = new("Segoe UI");
    internal static readonly IBrush Hover = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel _feedBox = new() { Tag = "friends-feed", IsVisible = false };
    /// <summary>Lines that were unread when this open first showed them: they keep the dot until the drawer folds.</summary>
    private readonly HashSet<string> _feedFresh = new(StringComparer.Ordinal);
    private FriendsFeed? _feedLive;
    private bool _feedMarking;
    private int _feedExtra;

    /// <summary>WPF AddFeed: the section is one persistent panel, re-added on every render.</summary>
    private void AddFeed()
    {
        _list.Children.Add(_feedBox);
        SafeFillFeed();
    }

    internal IReadOnlyList<string> FeedLineKeys => _feedBox.Children.OfType<Control>()
        .Select(c => c.Tag as string)
        .Where(t => t != null && t.StartsWith("friends-feed-line:", StringComparison.Ordinal))
        .Select(t => t!.Substring("friends-feed-line:".Length))
        .ToList();

    internal Control FeedSection => _feedBox;

    internal void ShowMoreFeed() { _feedExtra += FriendsFeedRules.MoreStep; SafeFillFeed(); }
    internal void ShowLessFeed() { _feedExtra = 0; SafeFillFeed(); }

    private void SafeFillFeed()
    {
        try { FillFeed(); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed paint failed: {E}", ex.Message); }
    }

    /// <summary>The drawer folded: the next open starts folded, and what was new this time is not.</summary>
    private void FeedFolded()
    {
        _feedFresh.Clear();
        _feedExtra = 0;
        Listen(null);
    }

    private FriendsFeed? CurrentFeed() { try { return FeedSource(); } catch { return null; } }

    /// <summary>Follows the feed while the drawer is open, and only then.</summary>
    private void Listen(FriendsFeed? feed)
    {
        var want = _isOpen ? feed : null;
        if (ReferenceEquals(want, _feedLive)) return;
        if (_feedLive != null) _feedLive.Changed -= OnFeedChanged;
        _feedLive = want;
        if (_feedLive != null) _feedLive.Changed += OnFeedChanged;
    }

    private void OnFeedChanged()
    {
        if (_feedMarking) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnFeedChanged); return; }
        if (!_isOpen) { Listen(null); return; }
        if (!_list.Children.Contains(_feedBox)) return;
        SafeFillFeed();
    }

    private bool IsNew(FeedEntry l) => !l.Read || _feedFresh.Contains(l.Key);

    /// <summary>Draws the section from the feed as it is now, and reads what it shows (WPF FillFeed).</summary>
    private void FillFeed()
    {
        _feedBox.Children.Clear();
        var feed = CurrentFeed();
        Listen(feed);
        var lines = feed?.Lines ?? Array.Empty<FeedEntry>();
        _feedBox.IsVisible = lines.Count > 0;
        if (lines.Count == 0) return;

        var fresh = lines.Count(IsNew);
        var show = Math.Min(lines.Count, FriendsFeedRules.FoldCount(lines.Count, fresh) + _feedExtra);
        _feedBox.Children.Add(FeedHead(fresh));

        var now = DateTime.UtcNow;
        var friends = _svc?.Snapshot?.Friends ?? (IReadOnlyList<Friend>)Array.Empty<Friend>();
        var shownUnread = new List<string>();
        for (var i = 0; i < show; i++)
        {
            var l = lines[i];
            var row = FeedRow(l.Event, IsNew(l), friends, now);
            if (friends.Any(f => f.Id == l.Event.FriendId)) MakeOpener(row, l.Event.FriendId);
            _feedBox.Children.Add(row);
            if (!l.Read) shownUnread.Add(l.Key);
        }

        var hidden = lines.Count - show;
        if (hidden > 0 || _feedExtra > 0)
        {
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 2) };
            if (hidden > 0)
            {
                var hiddenNew = lines.Skip(show).Count(IsNew);
                var more = FeedLink(hiddenNew > 0 ? Loc.GetF("friends_feed_more_new", hidden, hiddenNew) : Loc.GetF("friends_feed_more", hidden), "friends-feed-more");
                more.Click += (_, _) => ShowMoreFeed();
                foot.Children.Add(more);
            }
            if (_feedExtra > 0)
            {
                var less = FeedLink(Loc.Get("friends_feed_less"), "friends-feed-less");
                less.Click += (_, _) => ShowLessFeed();
                foot.Children.Add(less);
            }
            _feedBox.Children.Add(foot);
        }

        // Shown in an open drawer = read. A drawer built but never opened reads nothing.
        if (_isOpen && feed != null && shownUnread.Count > 0)
        {
            foreach (var k in shownUnread) _feedFresh.Add(k);
            _feedMarking = true;
            try { feed.MarkRead(shownUnread); }
            finally { _feedMarking = false; }
        }
    }

    private static Control FeedHead(int fresh)
    {
        var g = new Grid { Margin = new Thickness(8, 10, 8, 4), Tag = "friends-feed-head" };
        g.Children.Add(Tagged(Label(Loc.Get("friends_feed_title"), 10, Dim, Mono), "friends-feed-title"));
        if (fresh > 0)
        {
            var n = Tagged(Label(Loc.GetF("friends_feed_new", fresh), 10, Pink, Mono), "friends-feed-count");
            n.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(n);
        }
        return g;
    }

    private static Border FeedRow(FriendEvent e, bool isNew, IReadOnlyList<Friend> friends, DateTime nowUtc)
    {
        var g = new Grid { MinHeight = 22, ColumnDefinitions = new ColumnDefinitions("10,*,Auto") };
        var row = new Border
        {
            Margin = new Thickness(4, 0), Padding = new Thickness(4, 0), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Tag = "friends-feed-line:" + e.Key, Child = g,
        };
        if (isNew)
            g.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse
            {
                Width = 6, Height = 6, Fill = Pink, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Tag = "friends-feed-new",
            });
        var text = new TextBlock
        {
            FontFamily = Body, FontSize = 12, Foreground = isNew ? Text : Muted, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Tag = "friends-feed-text",
        };
        var plain = FillLine(text, e, friends, isNew);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var (key, n) = FriendsFeedRules.Ago(e.AtUtc, nowUtc);
        var ago = Tagged(Label(n is int x ? Loc.GetF(key, x) : Loc.Get(key), 10, Dim, Mono), "friends-feed-ago");
        ago.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(ago, 2);
        g.Children.Add(ago);
        // A long name trims; the whole line is one hover away.
        ToolTip.SetTip(row, plain);
        return row;
    }

    /// <summary>Fills <paramref name="t"/> with the line, the name in its own weight; returns it as plain text (WPF FillLine).</summary>
    internal static string FillLine(TextBlock t, FriendEvent e, IReadOnlyList<Friend> friends, bool isNew)
    {
        var line = FriendsFeedRules.Describe(e);
        var name = FriendsFeedRules.NameFor(e, friends) ?? Loc.Get("friends_feed_someone");
        var detail = line.Detail == null ? "" : line.DetailIsKey ? Loc.Get(line.Detail) : line.Detail;
        // Split on the name first, so a watch title that happens to hold "{0}" stays a title.
        var parts = Loc.Get(line.Key).Split("{0}");
        var plain = new System.Text.StringBuilder();
        t.Inlines ??= new InlineCollection();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Replace("{1}", detail);
            if (part.Length > 0) { t.Inlines.Add(new Run(part)); plain.Append(part); }
            if (i < parts.Length - 1)
            {
                t.Inlines.Add(new Run(name) { FontFamily = Display, FontWeight = FontWeight.SemiBold, Foreground = isNew ? Text : Lilac });
                plain.Append(name);
            }
        }
        return plain.ToString();
    }

    /// <summary>A line about someone on the list opens their card (Poke back is one press away).</summary>
    private void MakeOpener(Border row, string friendId)
    {
        (row.Cursor, row.Focusable) = (Hand(), true);
        row.PointerEntered += (_, _) => row.Background = Hover;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { e.Handled = true; OpenFromFeed(friendId); } };
        row.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; OpenFromFeed(friendId); } };
    }

    /// <summary>Opens <paramref name="friendId"/>'s card (WPF OpenOn: never folds it) and scrolls it into view.</summary>
    internal void OpenFromFeed(string friendId)
    {
        try
        {
            if (_openId != friendId) Toggle(friendId);
            // The row was just rebuilt: measure it before scrolling to it (WPF _list.UpdateLayout()).
            _list.UpdateLayout();
            _list.Children.OfType<Control>().FirstOrDefault(c => c.Tag as string == "friends-row:" + friendId)?.BringIntoView();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed open failed: {E}", ex.Message); }
    }

    private static Button FeedLink(string text, string tag)
    {
        var b = Pill(text, Brushes.Transparent, Lilac, tag);
        b.Padding = new Thickness(6, 2, 6, 2);
        if (b.Content is TextBlock t) t.FontSize = 11.5;
        // WPF FeedLink hover: FriendsLook.HoverBrush, ink unchanged (Fluent reads these per button on :pointerover / :pressed).
        foreach (var state in new[] { "PointerOver", "Pressed" })
            (b.Resources["ButtonBackground" + state], b.Resources["ButtonForeground" + state], b.Resources["ButtonBorderBrush" + state]) = (Hover, Lilac, Brushes.Transparent);
        return b;
    }

    // ---- the send trail (WPF FriendsDrawer.Trail.cs) -------------------------------------

    private void OnTrailsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnTrailsChanged); return; }
        try { if (_isOpen) Render(); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] trail repaint failed: {E}", ex.Message); }
    }

    /// <summary>The trail line for <paramref name="f"/>, or null when there is nothing recent or a send
    /// result is on the row already (it says the same thing for five seconds).</summary>
    private TextBlock? TrailLine(Friend f)
    {
        if (_results.ContainsKey(f.Id)) return null;
        SentTrail? t = null;
        try { t = _svc?.LastSentTo(f.Id); } catch { }
        if (t == null) return null;
        var (kindKey, stateKey) = FriendsDrawerRules.TrailKeys(t);
        var tone = ToneBrush(FriendsDrawerRules.ToneOf(t));
        var line = new TextBlock
        {
            FontSize = 10.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0), Tag = "friends-trail:" + t.State, Inlines = new InlineCollection(),
        };
        ToolTip.SetTip(line, Loc.Get(t.Kind == SendKind.Invite ? "friends_trail_tip_invite" : "friends_trail_tip"));
        line.Inlines.Add(new Run(Loc.Get(kindKey) + "  "));
        // WPF filled/hollow circle pips, Fluent circles here (fluent-icons 2026-10-10).
        for (int i = 0; i < t.Steps; i++)
        {
            bool lit = i < t.Lit;
            line.Inlines.Add(new InlineUIContainer(new IconGlyph
            {
                Kind = IconKind.Circle, Variant = lit ? IconVariant.Filled : IconVariant.Regular, Size = 8,
                Foreground = lit ? tone : Dim, Tag = lit ? "friends-trail-pip-lit" : "friends-trail-pip",
            }) { BaselineAlignment = BaselineAlignment.Center });
        }
        line.Inlines.Add(new Run("  " + Loc.Get(stateKey)) { Foreground = tone });
        return line;
    }

    private static IBrush ToneBrush(FriendsDrawerRules.TrailTone tone) => tone switch
    {
        FriendsDrawerRules.TrailTone.Seen => Lilac,
        FriendsDrawerRules.TrailTone.Yes => Mint,
        FriendsDrawerRules.TrailTone.No => Gold,
        FriendsDrawerRules.TrailTone.Quiet => Dim,
        _ => Muted,
    };

    /// <summary>The trail line's words (lit pips as "+", unlit as ".") showing for a friend, for the suite.</summary>
    internal string? TrailTextFor(string friendId)
    {
        var row = _list.Children.OfType<Control>().FirstOrDefault(c => c.Tag as string == "friends-row:" + friendId);
        var tb = row?.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(t => (t.Tag as string)?.StartsWith("friends-trail:", StringComparison.Ordinal) == true);
        if (tb?.Inlines == null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var inline in tb.Inlines)
            if (inline is Run r) sb.Append(r.Text);
            else if (inline is InlineUIContainer { Child: IconGlyph gl }) sb.Append(gl.Variant == IconVariant.Filled ? "+" : ".");
        return sb.ToString();
    }
}
