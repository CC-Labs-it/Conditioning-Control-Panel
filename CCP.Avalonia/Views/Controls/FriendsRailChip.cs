// PORTED from ConditioningControlPanel/Controls/Friends/FriendsRailChip.cs: your face and name at the
// foot of the rail with a mint pill for friends online; a click opens the drawer upward over the rail.
// The pink unread badge counts the friends feed (WPF UpdateBadge).
// ponytail: no bump / hover lift, no tier plate, no
// rail hold; Popup light-dismiss stands in for WPF's click-outside and host-moved watchers.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed class FriendsRailChip : Grid
{
    private readonly Border _pill = new(), _badge = new(), _face = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(14) };
    private readonly TextBlock _pillText = new(), _badgeText = new(), _name;
    private FriendsFeed? _feed;
    private readonly Popup _popup;
    private IFriendsService? _svc;
    public FriendsRailChip() : this(null) { }
    internal FriendsRailChip(IFriendsService? service)
    {
        Drawer = new FriendsDrawer(service);
        (Height, Margin, Background, Cursor) = (48, new Thickness(0, 2, 0, 4), Brushes.Transparent, FriendsDrawer.Hand());
        ColumnDefinitions = new ColumnDefinitions("56,*");
        (_pillText.FontFamily, _pillText.FontWeight, _pillText.FontSize, _pillText.Foreground) = (FriendsDrawer.Display, FontWeight.SemiBold, 11, FriendsDrawer.MintInk);
        (_pill.Child, _pill.Background, _pill.CornerRadius, _pill.Padding, _pill.IsVisible, _pill.Tag) = (_pillText, FriendsDrawer.Mint, new CornerRadius(999), new Thickness(5, 0), false, "friends-chip-online");
        (_pill.HorizontalAlignment, _pill.VerticalAlignment, _pill.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -2, -4, 0));
        _face.HorizontalAlignment = HorizontalAlignment.Center;
        // WPF FriendsRailChip.cs:111-128: the pink unread badge, bottom right of the face.
        (_badgeText.FontFamily, _badgeText.FontWeight, _badgeText.FontSize, _badgeText.HorizontalAlignment, _badgeText.Foreground) =
            (FriendsDrawer.Display, FontWeight.SemiBold, 11, HorizontalAlignment.Center, new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x1A)));
        (_badge.Child, _badge.Background, _badge.CornerRadius, _badge.Padding, _badge.MinWidth, _badge.IsVisible, _badge.Tag) =
            (_badgeText, FriendsDrawer.Pink, new CornerRadius(999), new Thickness(5, 0), 18, false, "friends-chip-unread");
        (_badge.BorderThickness, _badge.BorderBrush) = (new Thickness(2), new SolidColorBrush(Color.FromRgb(0x0E, 0x09, 0x19)));
        (_badge.HorizontalAlignment, _badge.VerticalAlignment, _badge.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -4, -2));
        _face.Child = new Grid { Children = { FriendsDrawer.Avatar(Drawer.MeName(), 40, null), _pill, _badge } };
        // The name column is 0 px wide while the rail is shut, so nothing in it may wrap.
        _name = FriendsDrawer.Label(Drawer.MeName(), 14, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        _name.Margin = new Thickness(0, 0, 10, 0);
        // WPF e9242c3d0: the name already sits in a star column and Label trims with an ellipsis; the tooltip carries it whole.
        Grid.SetColumn(_name, 1);
        Children.AddRange(new Control[] { _face, _name });
        UpdateBadge();
        _popup = new Popup
        {
            Child = Drawer, Placement = PlacementMode.Top, PlacementTarget = this, HorizontalOffset = 4, VerticalOffset = -6,
            IsLightDismissEnabled = true,
        };
        Children.Add(_popup);
        _popup.Opened += (_, _) => { _face.Background = FriendsDrawer.Raised; Drawer.OnOpened(); };
        _popup.Closed += (_, _) => { _face.Background = Brushes.Transparent; Drawer.OnClosed(); };
        Drawer.OwnerWindow = () => TopLevel.GetTopLevel(this) as Window;
        Drawer.CloseRequested += () => _popup.IsOpen = false;
        Drawer.SettingsRequested += () => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("appsettings");
        Drawer.SignInRequested += () => { _popup.IsOpen = false; _ = (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenUnifiedLoginDialog(); };
        Drawer.InvitesRequested += () => { _popup.IsOpen = false; (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenInvitesCard(); };
        // Avalonia routes the popup's input through the Popup to this chip; a click inside the drawer is not a chip click.
        PointerReleased += (_, e) =>
        {
            if (e.Source is Visual s && (ReferenceEquals(s, Drawer) || Drawer.IsVisualAncestorOf(s))) return;
            if (e.InitialPressMouseButton == MouseButton.Left) { Toggle(); e.Handled = true; }
        };
        AttachedToVisualTree += (_, _) => { Rebind(); LocalizationManager.Instance.LanguageChanged += OnLanguage; };
        DetachedFromVisualTree += (_, _) => { _popup.IsOpen = false; Unwire(); Drawer.Unsubscribe(); LocalizationManager.Instance.LanguageChanged -= OnLanguage; };
        Rebind();
    }
    internal FriendsDrawer Drawer { get; }
    internal bool IsOpen => _popup.IsOpen;

    /// <summary>The rail trims the name, so the tooltip leads with it in full (WPF UpdateTooltip).</summary>
    /// <summary>WPF UpdateTooltip runs on a language switch too: the tooltip is code-set, so rebuild it.</summary>
    private void OnLanguage(object? sender, EventArgs e) => UpdateBadge();

    internal static string Tip(string? name, int unread = 0) =>
        (string.IsNullOrWhiteSpace(name) ? "" : name + "\n") + Loc.Get("friends_chip_tooltip")
        + (unread > 0 ? "\n" + Loc.GetF("friends_feed_new", unread) : "");

    /// <summary>The number the pink unread badge shows, 0 while hidden, 10 for "9+".</summary>
    internal int UnreadBadge => !_badge.IsVisible ? 0 : _badgeText.Text == "9+" ? 10 : int.TryParse(_badgeText.Text, out var n) ? n : 0;

    /// <summary>WPF UpdateBadge: the feed's unread count, and the tooltip says it.</summary>
    internal void UpdateBadge()
    {
        int n = 0;
        try { if (_svc?.Available != false) n = _feed?.Unread ?? 0; } catch { }
        _badgeText.Text = FriendsFeedRules.BadgeText(n);
        _badge.IsVisible = n > 0;
        ToolTip.SetTip(this, Tip(_name.Text, n));
    }
    private void OnFeedChanged()
    {
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { global::Avalonia.Threading.Dispatcher.UIThread.Post(OnFeedChanged); return; }
        UpdateBadge();
    }

    internal int PillCount => _pill.IsVisible && int.TryParse(_pillText.Text, out var n) ? n : 0;
    internal void Toggle()
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        Rebind();
        _popup.IsOpen = true;
    }

    private void Rebind()
    {
        var next = Drawer.Service ?? Platform.FriendsHead.Service;
        if (!ReferenceEquals(next, _svc)) { Unwire(); _svc = next; if (_svc != null) _svc.SnapshotChanged += OnSnapshot; }
        // WPF RebindFeed: the feed is re-read on every rebind in case the chip was built before it.
        FriendsFeed? feed;
        try { feed = Drawer.FeedSource(); } catch { feed = null; }
        if (!ReferenceEquals(feed, _feed))
        {
            if (_feed != null) _feed.Changed -= OnFeedChanged;
            _feed = feed;
            if (_feed != null) _feed.Changed += OnFeedChanged;
        }
        UpdatePill();
    }
    private void Unwire()
    {
        if (_svc != null) _svc.SnapshotChanged -= OnSnapshot;
        if (_feed != null) _feed.Changed -= OnFeedChanged;
        (_svc, _feed) = (null, null);
    }
    private void OnSnapshot(FriendsSnapshot _) => UpdatePill();
    internal void UpdatePill()
    {
        int n = 0;
        try { if (_svc?.Available == true) n = _svc.Snapshot?.OnlineCount ?? 0; } catch { }
        _pillText.Text = n.ToString();
        _pill.IsVisible = n > 0;
        // A snapshot change can be an account change: the feed then counts another account's lines.
        UpdateBadge();
    }
}
