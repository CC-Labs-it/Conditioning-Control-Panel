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

// WPF FriendsRailChip: the fixed Avalonia rail needs no folding-rail hold latch.
public sealed partial class FriendsRailChip : Grid
{
    private readonly Border _pill = new(), _face = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(14) };
    private readonly Border _badge = new();
    private readonly TextBlock _pillText = new(), _badgeText = new(), _name;
    private FriendsFeed? _feed;
    private int _lastUnread = -1;
    private readonly Popup _popup;
    private IFriendsService? _svc;
    private readonly Grid _nameLine = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
    private int _lastOnline = -1;
    private DateTime _closedAt = DateTime.MinValue;
    public FriendsRailChip() : this(null) { }
    /// <summary>The suite passes its own service and feed (WPF FriendsRailChip(service, feed)); the app passes nothing.</summary>
    internal FriendsRailChip(IFriendsService? service, FriendsFeed? feed = null)
    {
        Drawer = new FriendsDrawer(service);
        if (feed != null) Drawer.FeedSource = () => feed;
        (Height, Margin, Background, Cursor) = (48, new Thickness(0, 2, 0, 4), Brushes.Transparent, FriendsDrawer.Hand());
        ColumnDefinitions = new ColumnDefinitions("56,*");
        (_pillText.FontFamily, _pillText.FontWeight, _pillText.FontSize, _pillText.Foreground) = (FriendsDrawer.Display, FontWeight.SemiBold, 11, FriendsDrawer.MintInk);
        (_pill.Child, _pill.Background, _pill.CornerRadius, _pill.Padding, _pill.IsVisible, _pill.Tag) = (_pillText, FriendsDrawer.Mint, new CornerRadius(999), new Thickness(5, 0), false, "friends-chip-online");
        (_pill.HorizontalAlignment, _pill.VerticalAlignment, _pill.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -2, -4, 0));
        // WPF FriendsRailChip.cs:111-131: the pink unread badge (the friends feed), bottom right of the face.
        (_badgeText.FontFamily, _badgeText.FontWeight, _badgeText.FontSize, _badgeText.Foreground, _badgeText.HorizontalAlignment) = (FriendsDrawer.Display, FontWeight.SemiBold, 11, new SolidColorBrush(Color.Parse("#2A061A")), HorizontalAlignment.Center);
        (_badge.Child, _badge.Background, _badge.CornerRadius, _badge.Padding, _badge.MinWidth, _badge.IsVisible, _badge.Tag) = (_badgeText, FriendsDrawer.Pink, new CornerRadius(999), new Thickness(5, 0), 18, false, "friends-chip-unread");
        (_badge.HorizontalAlignment, _badge.VerticalAlignment, _badge.Margin) = (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -4, -2));
        (_badge.BorderThickness, _badge.BorderBrush) = (new Thickness(2), new SolidColorBrush(Color.Parse("#0E0919")));
        (_badge.RenderTransform, _badge.RenderTransformOrigin) = (new ScaleTransform(1, 1), RelativePoint.Center);
        _badge.BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.Parse("#B3FF5FB4") });
        _pill.BorderThickness = new Thickness(2);
        _pill.BorderBrush = new SolidColorBrush(Color.Parse("#0E0919"));
        _pill.RenderTransform = new ScaleTransform(1, 1);
        _pill.RenderTransformOrigin = RelativePoint.Center;
        _pill.BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.Parse("#B35FFFD0") });
        _face.RenderTransform = new ScaleTransform(1, 1);
        _face.RenderTransformOrigin = RelativePoint.Center;
        _face.BorderThickness = new Thickness(1);
        _face.HorizontalAlignment = HorizontalAlignment.Center;
        _face.Child = new Grid { Children = { FriendsDrawer.Avatar(Drawer.MeName(), 40, null), _pill, _badge } };
        // The star column must trim the name while the Auto column keeps the tier plate.
        _name = FriendsDrawer.Label(Drawer.MeName(), 14, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        _nameLine.Children.Add(_name);
        // WPF e9242c3d0: the name already sits in a star column and Label trims with an ellipsis; the tooltip carries it whole.
        Grid.SetColumn(_nameLine, 1);
        Children.AddRange(new Control[] { _face, _nameLine });
        ToolTip.SetTip(this, Tip(_name.Text));
        _popup = new Popup
        {
            Child = Drawer, Placement = PlacementMode.Top, PlacementTarget = this, HorizontalOffset = 4, VerticalOffset = -6,
            IsLightDismissEnabled = true,
        };
        Children.Add(_popup);
        _popup.Opened += (_, _) => { _face.Background = FriendsDrawer.Raised; _face.BorderBrush = FriendsDrawer.Line2; DropTopmost(); Platform.FriendsLanding.DrawerOpen(); Drawer.OnOpened(); };
        _popup.Closed += (_, _) => { _closedAt = DateTime.UtcNow; _face.Background = Brushes.Transparent; _face.BorderBrush = Brushes.Transparent; Platform.FriendsLanding.DrawerClose(); Drawer.OnClosed(); };
        WireInput();
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
        AttachedToVisualTree += (_, _) => { BindHost(); Rebind(); RefreshFace(); LocalizationManager.Instance.LanguageChanged += OnLanguage; };
        DetachedFromVisualTree += (_, _) => { _popup.IsOpen = false; UnbindHost(); CancelBumps(); Unwire(); UnwireFeed(); Drawer.Unsubscribe(); LocalizationManager.Instance.LanguageChanged -= OnLanguage; };
        Rebind();
    }
    internal FriendsDrawer Drawer { get; }
    internal bool IsOpen => _popup.IsOpen;

    /// <summary>WPF UpdateTooltip runs on a language switch too: the tooltip is code-set, so rebuild it.</summary>
    private void OnLanguage(object? sender, EventArgs e) => ToolTip.SetTip(this, Tip(_name.Text, UnreadBadge));

    /// <summary>The rail trims the name, so the tooltip leads with it in full (WPF UpdateTooltip).</summary>

    internal static string Tip(string? name, int unread = 0) =>
        (string.IsNullOrWhiteSpace(name) ? "" : name + "\n") + Loc.Get("friends_chip_tooltip")
        + (unread > 0 ? "\n" + Loc.GetF("friends_feed_new", unread) : "");

    internal int PillCount => _pill.IsVisible && int.TryParse(_pillText.Text, out var n) ? n : 0;
    internal void Toggle()
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        if (DateTime.UtcNow - _closedAt < TimeSpan.FromMilliseconds(250)) return;
        Rebind(); RefreshFace();
        _popup.IsOpen = true;
    }

    private void Rebind()
    {
        var next = Drawer.Service ?? Platform.FriendsHead.Service;
        if (!ReferenceEquals(next, _svc)) { Unwire(); _svc = next; if (_svc != null) _svc.SnapshotChanged += OnSnapshot; }
        RebindFeed();
        UpdatePill(false);
    }
    private void Unwire() { if (_svc != null) _svc.SnapshotChanged -= OnSnapshot; _svc = null; }
    private void OnSnapshot(FriendsSnapshot _) => Ui(() => UpdatePill(true));
    private static void Ui(Action action) { if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action(); else global::Avalonia.Threading.Dispatcher.UIThread.Post(action); }
    internal void UpdatePill(bool animate = true)
    {
        int n = 0;
        try { if (_svc?.Available == true) n = _svc.Snapshot?.OnlineCount ?? 0; } catch { }
        _pillText.Text = n.ToString();
        _pill.IsVisible = n > 0;
        if (animate && _lastOnline >= 0 && n > _lastOnline) Bump(_pill);
        _lastOnline = n;
        // A snapshot change can be an account change: the feed then counts another account's lines.
        UpdateBadge(animate);
    }

    /// <summary>The number the pink unread badge shows, 0 while it is hidden, 10 for "9+".</summary>
    internal int UnreadBadge => !_badge.IsVisible ? 0 : _badgeText.Text == "9+" ? 10 : int.TryParse(_badgeText.Text, out var u) ? u : 0;
    /// <summary>Follows the drawer's feed (WPF App.FriendsFeed); re-read on every rebind in case the chip was built before it.</summary>
    private void RebindFeed()
    {
        FriendsFeed? next;
        try { next = Drawer.FeedSource(); } catch { next = null; }
        if (ReferenceEquals(next, _feed)) return;
        UnwireFeed();
        _feed = next;
        if (_feed != null) _feed.Changed += OnFeedChanged;
    }
    private void UnwireFeed() { if (_feed != null) _feed.Changed -= OnFeedChanged; _feed = null; }
    private void OnFeedChanged() => Ui(() => UpdateBadge(true));
    internal void UpdateBadge(bool animate)
    {
        int n = 0;
        try { if (_svc?.Available != false) n = _feed?.Unread ?? 0; } catch { }
        _badgeText.Text = FriendsFeedRules.BadgeText(n);
        _badge.IsVisible = n > 0;
        ToolTip.SetTip(this, Tip(_name.Text, n));
        if (animate && n > _lastUnread && _lastUnread >= 0 && n > 0) Bump(_badge);
        _lastUnread = n;
    }
    internal void RefreshFace()
    {
        _name.Text = Drawer.MeName();
        if (_face.Child is Grid face) face.Children[0] = FriendsDrawer.Avatar(_name.Text!, 40, null);
        while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
        var tier = ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.DisplayTier(Drawer.MeTier());
        if (tier > 0 && Helpers.ModArt.TryLoad($"features/tier_badge_t{tier}.png", 96) is { } art)
        {
            var plate = new Image { Source = art, Height = 13, Stretch = Stretch.Uniform, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = $"friends-tier-{tier}" };
            Grid.SetColumn(plate, 1); _nameLine.Children.Add(plate);
        }
        UpdatePill(false);
    }
}
