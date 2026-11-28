using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// "WHAT HAPPENED": the friends feed (<see cref="FriendsFeed"/>) at the top of the list, right
/// under the leash. RenderList calls <see cref="AddFeed"/> right after the leash section. Newest
/// first, one short line each, with how long ago. The section folds (owner 2026-11-22): folded it
/// shows at most two recent lines (a read line leaves after a day, the header's x hides them now),
/// open it shows the whole list in pages. Opening the drawer reads every line: a new one keeps its
/// pink dot until the drawer folds, and the badges on the chips and the tray drop at once. Nothing
/// to tell, no section at all.
///
/// <para>The section is one persistent panel, re-added on every render like the leash section, and
/// refilled on its own when a line lands while the drawer is open, so a feed line never rebuilds
/// the friend list under the player's pointer.</para>
/// </summary>
public sealed partial class FriendsDrawer
{
    partial void AddFeed();

    /// <summary>The feed this drawer draws: the app's own. Swappable for the suite.</summary>
    internal static Func<FriendsFeed?> FeedSource { get; set; } = () => App.FriendsFeed;

    private readonly StackPanel _feedBox = new() { Tag = "friends-feed", Visibility = Visibility.Collapsed };

    /// <summary>Lines that were unread when this open first showed them. They keep the dot until
    /// the drawer folds, although the feed already counts them read.</summary>
    private readonly HashSet<string> _feedFresh = new(StringComparer.Ordinal);

    /// <summary>The feed this drawer listens to while it is open, or null.</summary>
    private FriendsFeed? _feedLive;
    private bool _feedHooked;
    private bool _feedMarking;

    /// <summary>Lines shown past the fold ("more" pressed).</summary>
    private int _feedExtra;

    /// <summary>The newest line last drawn: a different one on top slides in.</summary>
    private string? _feedTop;

    partial void AddFeed()
    {
        if (!_feedHooked)
        {
            _feedHooked = true;
            IsVisibleChanged += OnFeedVisibility;
        }
        _list.Children.Add(_feedBox);
        SafeFillFeed();
    }

    /// <summary>The keys of the lines drawn, top to bottom. The suite reads it.</summary>
    internal IReadOnlyList<string> FeedLineKeys => _feedBox.Children.OfType<FrameworkElement>()
        .Select(e => e.Tag as string)
        .Where(t => t != null && t.StartsWith("friends-feed-line:", StringComparison.Ordinal))
        .Select(t => t!.Substring("friends-feed-line:".Length))
        .ToList();

    /// <summary>The section itself (collapsed while the feed is empty). The suite reads it.</summary>
    internal FrameworkElement FeedSection => _feedBox;

    /// <summary>"more": the next few lines.</summary>
    internal void ShowMoreFeed()
    {
        _feedExtra += FriendsFeedRules.MoreStep;
        SafeFillFeed();
    }

    /// <summary>"less": back to the fold.</summary>
    internal void ShowLessFeed()
    {
        _feedExtra = 0;
        SafeFillFeed();
    }

    private void SafeFillFeed()
    {
        try { FillFeed(); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] feed paint failed: {E}", ex.Message); }
    }

    /// <summary>The drawer folded: the next open starts folded, and what was new this time is not.</summary>
    internal void FeedFolded()
    {
        _feedFresh.Clear();
        _feedExtra = 0;
        Listen(null);
    }

    private void OnFeedVisibility(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) FeedFolded();
    }

    private static FriendsFeed? CurrentFeed()
    {
        try { return FeedSource(); }
        catch { return null; }
    }

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
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnFeedChanged));
            return;
        }
        if (!_isOpen) { Listen(null); return; }
        if (!_list.Children.Contains(_feedBox)) return;
        SafeFillFeed();
    }

    private bool IsNew(FeedEntry l) => !l.Read || _feedFresh.Contains(l.Key);

    /// <summary>"What happened" folded or open. Internal for the suite.</summary>
    internal bool FeedOpen => FriendsFold.State.FeedOpen;

    /// <summary>Opens or folds the section (the header, and "N more" under a folded preview).</summary>
    internal void ToggleFeed()
    {
        FriendsFold.Update(s => s.FeedOpen = !s.FeedOpen);
        _feedExtra = 0;
        SafeFillFeed();
        if (FeedOpen) RiseIn(FeedRows());
    }

    /// <summary>The header's x: the preview empties now (the open list still has every line).</summary>
    internal void ClearFeedPreview(bool animate = true)
    {
        var feed = CurrentFeed();
        var lines = feed?.Lines ?? Array.Empty<FeedEntry>();
        if (lines.Count == 0) return;
        var newest = lines.Max(l => l.Event.AtUtc);
        FadeAway(animate ? FeedRows() : new List<FrameworkElement>(), () =>
        {
            FriendsFold.Update(s => s.FeedClearedUtc = newest);
            SafeFillFeed();
        });
    }

    private List<FrameworkElement> FeedRows() => _feedBox.Children.OfType<FrameworkElement>()
        .Where(e => e.Tag is string t && t.StartsWith("friends-feed-line:", StringComparison.Ordinal)).ToList();

    /// <summary>Draws the section from the feed as it is now. Folded: a header and at most
    /// <see cref="FriendsFold.FeedPreview"/> recent lines; open: every line, newest first, in pages.
    /// An open drawer reads every line at once (the chip's bubble drops), and the new ones keep
    /// their dot until the drawer folds.</summary>
    private void FillFeed()
    {
        _feedBox.Children.Clear();
        var feed = CurrentFeed();
        Listen(feed);
        var lines = feed?.Lines ?? Array.Empty<FeedEntry>();
        if (lines.Count == 0)
        {
            _feedBox.Visibility = Visibility.Collapsed;
            return;
        }
        _feedBox.Visibility = Visibility.Visible;

        var fold = FriendsFold.State;
        bool open = fold.FeedOpen;
        var now = DateTime.UtcNow;
        var fresh = lines.Count(IsNew);
        IReadOnlyList<FeedEntry> shown = open
            ? lines.Take(Math.Min(lines.Count, FriendsFeedRules.MoreStep + _feedExtra)).ToList()
            : FriendsFold.FeedPreviewLines(lines, IsNew, now, fold.FeedClearedUtc);

        Button? clear = null;
        if (!open && shown.Count > 0)
        {
            clear = XButton("friends-feed-clear", Loc.Get("friends_feed_clear"));
            clear.Click += (_, _) => { FriendsSfx.Click(); ClearFeedPreview(); };
        }
        var count = fresh > 0
            ? FriendsLook.Label(Loc.GetF("friends_feed_new", fresh), 10, FriendsLook.PinkBrush, FriendsLook.Mono)
            : FriendsLook.Label(lines.Count.ToString(), 10, FriendsLook.DimBrush, FriendsLook.Mono);
        count.Tag = fresh > 0 ? "friends-feed-count" : "friends-feed-total";
        _feedBox.Children.Add(FoldHead("feed", Loc.Get("friends_feed_title"), "friends-feed-title", open, ToggleFeed,
            right: count, extra: clear));

        var friends = _last.Friends;
        for (var i = 0; i < shown.Count; i++)
        {
            var l = shown[i];
            var row = FeedRow(l.Event, IsNew(l), friends, now);
            if (friends.Any(f => f.Id == l.Event.FriendId)) MakeOpener(row, l.Event.FriendId);
            _feedBox.Children.Add(row);
            if (i == 0 && l.Key == lines[0].Key && _isOpen && _feedTop != null && _feedTop != l.Key) LandLine(row);
        }
        _feedTop = lines[0].Key;

        var rest = lines.Count - shown.Count;
        if (rest > 0 || (open && _feedExtra > 0))
        {
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 2) };
            if (rest > 0)
            {
                var shownKeys = new HashSet<string>(shown.Select(l => l.Key), StringComparer.Ordinal);
                var restNew = lines.Count(l => !shownKeys.Contains(l.Key) && IsNew(l));
                var more = FeedLink(restNew > 0
                    ? Loc.GetF("friends_feed_more_new", rest, restNew)
                    : Loc.GetF("friends_feed_more", rest), "friends-feed-more");
                // Folded, "more" opens the whole list; open, it pages further down it.
                more.Click += (_, _) => { FriendsSfx.Click(); if (open) ShowMoreFeed(); else ToggleFeed(); };
                foot.Children.Add(more);
            }
            if (open && _feedExtra > 0)
            {
                var less = FeedLink(Loc.Get("friends_feed_less"), "friends-feed-less");
                less.Click += (_, _) => { FriendsSfx.Click(); ShowLessFeed(); };
                foot.Children.Add(less);
            }
            _feedBox.Children.Add(foot);
        }

        // An open drawer = read. A drawer built but never opened reads nothing.
        var unread = lines.Where(l => !l.Read).Select(l => l.Key).ToList();
        if (_isOpen && feed != null && unread.Count > 0)
        {
            foreach (var k in unread) _feedFresh.Add(k);
            _feedMarking = true;
            try { feed.MarkRead(unread); }
            finally { _feedMarking = false; }
        }
    }

    private static Border FeedRow(FriendEvent e, bool isNew, IReadOnlyList<Friend> friends, DateTime nowUtc)
    {
        var g = new Grid { MinHeight = 22 };
        var row = new Border
        {
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(4, 0, 4, 0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Tag = "friends-feed-line:" + e.Key,
            Child = g,
        };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (isNew)
        {
            g.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = FriendsLook.PinkBrush,
                Effect = FriendsLook.Glow(FriendsLook.Pink, 6, 0.9),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "friends-feed-new",
            });
        }

        var text = new TextBlock
        {
            FontFamily = FriendsLook.Body,
            FontSize = 12,
            Foreground = isNew ? FriendsLook.TextBrush : FriendsLook.MutedBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "friends-feed-text",
        };
        var plain = FillLine(text, e, friends, isNew);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        var (key, n) = FriendsFeedRules.Ago(e.AtUtc, nowUtc);
        var ago = FriendsLook.Label(n is int x ? Loc.GetF(key, x) : Loc.Get(key), 10, FriendsLook.DimBrush, FriendsLook.Mono);
        ago.Margin = new Thickness(8, 0, 0, 0);
        ago.Tag = "friends-feed-ago";
        Grid.SetColumn(ago, 2);
        g.Children.Add(ago);

        // A long name trims; the whole line is one hover away.
        row.ToolTip = plain;
        return row;
    }

    /// <summary>Fills <paramref name="t"/> with the line, the name in its own weight, and returns
    /// the same line as plain text.</summary>
    internal static string FillLine(TextBlock t, FriendEvent e, IReadOnlyList<Friend> friends, bool isNew)
    {
        var line = FriendsFeedRules.Describe(e);
        var name = FriendsFeedRules.NameFor(e, friends) ?? Loc.Get("friends_feed_someone");
        var detail = line.Detail == null ? "" : line.DetailIsKey ? Loc.Get(line.Detail) : line.Detail;
        // Split on the name first, so a watch title that happens to hold "{0}" stays a title.
        var parts = Loc.Get(line.Key).Split("{0}");
        var plain = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Replace("{1}", detail);
            if (part.Length > 0)
            {
                t.Inlines.Add(new Run(part));
                plain.Append(part);
            }
            if (i < parts.Length - 1)
            {
                t.Inlines.Add(new Run(name)
                {
                    FontFamily = FriendsLook.Display,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isNew ? FriendsLook.TextBrush : FriendsLook.LilacBrush,
                });
                plain.Append(name);
            }
        }
        return plain.ToString();
    }

    /// <summary>A line about someone on the list opens their card (Poke back is one press away).</summary>
    private void MakeOpener(Border row, string friendId)
    {
        row.Cursor = System.Windows.Input.Cursors.Hand;
        row.MouseEnter += (_, _) => row.Background = FriendsLook.HoverBrush;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            FriendsSfx.Click();
            OpenFromFeed(friendId);
        };
    }

    /// <summary>Opens <paramref name="friendId"/>'s card and scrolls it into view.</summary>
    internal void OpenFromFeed(string friendId)
    {
        try
        {
            OpenOn(friendId);
            if (RowFor(friendId) is { } row)
            {
                _list.UpdateLayout();
                row.BringIntoView();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] feed open failed: {E}", ex.Message); }
    }

    /// <summary>A line that just landed slides in from the left with its dot. Still under Motion Off.</summary>
    private static void LandLine(FrameworkElement row)
    {
        var k = Amount;
        if (k <= 0) return;
        var dur = TimeSpan.FromMilliseconds(280);
        var slide = new TranslateTransform(-12 * k, 0);
        row.RenderTransform = slide;
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-12 * k, 0, dur) { EasingFunction = new BackEase { Amplitude = 0.4 * k, EasingMode = EasingMode.EaseOut } });
    }

    private static Button FeedLink(string text, string tag)
    {
        var b = FriendsLook.Pill(text, Brushes.Transparent, FriendsLook.LilacBrush, Brushes.Transparent, 8,
            new Thickness(6, 2, 6, 2), FriendsLook.HoverBrush);
        b.FontSize = 11.5;
        b.Tag = tag;
        return b;
    }
}
