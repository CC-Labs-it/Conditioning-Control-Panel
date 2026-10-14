using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>How a knock card ended (WPF KnockCard.cs:18). NotNow answers the sender (declined);
/// Later only puts the card away (it waits in the Inbox while it is answerable).</summary>
internal enum KnockOutcome { Go, NotNow, Later, RanOut }

/// <summary>
/// PORTED from ConditioningControlPanel/Windows/Friends/KnockCard.cs: an invite at the door. Avatar and
/// name, one line, the destination's picture, Join / Not now, and a countdown with a ring that runs down
/// to the invite's own expiry. Rings full size for <see cref="LandingRules.KnockRingSeconds"/>, then tucks
/// to a strip; hovering opens it again; the x puts it away unanswered. Owned, topmost, never activates.
/// Cards stack down the owner's top-right corner, newest at the bottom.
/// </summary>
internal sealed class KnockCard : Window
{
    private const double CardWidth = 232, TuckedWidth = 206, RingSize = 22;   // WPF :31-33
    // WPF's card Margin bottom 12 (:155) as stack spacing: a transparent margin inside this window would take
    // clicks over the panel (no per-pixel click-through here), so the gap lives between windows instead.
    private const double Gap = 12;
    private static readonly IBrush Ground = new SolidColorBrush(Color.FromRgb(0x22, 0x16, 0x41));
    private static readonly List<KnockCard> Open = new();

    /// <summary>The server's clock (WPF ServerClock.UtcNow); tests step it.</summary>
    internal static Func<DateTimeOffset> Clock { get; set; } = () => ServerClock.UtcNow;

    internal InboxItem Item { get; }
    private readonly DateTimeOffset _start, _end;
    private readonly Path _arc = new();
    internal readonly TextBlock Countdown = new();
    private readonly DispatcherTimer _tick;
    private readonly Action<InboxItem, KnockOutcome> _done;
    private readonly Action<InboxItem>? _shown;
    private readonly Window? _anchor;
    private readonly Border _card;
    private readonly Control _line, _art;
    private readonly TranslateTransform _shift = new();
    private DateTimeOffset _shownAt;
    private bool _folded, _tucked, _hovered;
    private int _lastSecond = -1;
    // The running tween: enter (0) or fold (1), from the first frame's time.
    private int _anim = -1;
    private TimeSpan? _animStart;

    internal bool IsTucked => _tucked && !_hovered;
    internal Button? GoButton { get; }
    internal Button NotNowButton { get; }
    internal Button LaterButton { get; }

    /// <summary>--render-all only: a Ramp invite (a destination this head can Join), two minutes left.</summary>
    internal KnockCard() : this(null, new InboxItem("render", SendKind.Invite, "f", "Bambi", null, null, InviteDestination.Ramp, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2)), "invites you to a Ramp link", "Join", "Not now", "Answer later", (_, _) => { }, null) { }

    private KnockCard(Window? anchor, InboxItem item, string line, string? goLabel, string notNowLabel, string laterTip,
        Action<InboxItem, KnockOutcome> done, Action<InboxItem>? shown)
    {
        // WPF LandingChrome.Dress: borderless, transparent, no taskbar, never activated, topmost, sized to content.
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _anchor = anchor;
        Item = item;
        _done = done;
        _shown = shown;
        var now = Clock();
        _start = LandingRules.KnockStarts(item, now);
        _end = LandingRules.KnockEnds(item, now);
        _shownAt = now;

        // The clock sits beside the name so no button label can push it off the card (WPF :67-79).
        var clock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        Countdown.FontFamily = FriendsDrawer.Mono;
        Countdown.FontSize = 11;
        Countdown.Foreground = FriendsDrawer.Muted;
        Countdown.VerticalAlignment = VerticalAlignment.Center;
        Countdown.Margin = new Thickness(0, 0, 6, 0);
        clock.Children.Add(Countdown);
        clock.Children.Add(BuildRing());
        DockPanel.SetDock(clock, Dock.Right);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        // ponytail: initials only, like the drawer; WPF LandingChrome.Avatar also fetched the proxy picture.
        top.Children.Add(FriendsDrawer.Avatar(item.FromName, 28, null));
        var words = new StackPanel { Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        head.Children.Add(clock);
        head.Children.Add(new TextBlock
        {
            Text = item.FromName, FontFamily = FriendsDrawer.Display, FontSize = 13.5, FontWeight = FontWeight.SemiBold,
            Foreground = FriendsDrawer.Text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = CardWidth - 84,
            VerticalAlignment = VerticalAlignment.Center,
        });
        words.Children.Add(head);
        _line = new TextBlock { Text = line, FontSize = 11, Foreground = FriendsDrawer.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = CardWidth - 84 };
        words.Children.Add(_line);
        Grid.SetColumn(words, 1);
        top.Children.Add(words);

        LaterButton = FriendsDrawer.Pill(new IconGlyph { Kind = IconKind.Dismiss, Size = 10 }, Brushes.Transparent, FriendsDrawer.Muted, "knock-later");
        LaterButton.Padding = new Thickness(5);
        LaterButton.BorderThickness = new Thickness(0);
        LaterButton.VerticalAlignment = VerticalAlignment.Top;
        ToolTip.SetTip(LaterButton, laterTip);
        global::Avalonia.Automation.AutomationProperties.SetName(LaterButton, laterTip);
        LaterButton.Click += (_, _) => Fold(KnockOutcome.Later);
        Grid.SetColumn(LaterButton, 2);
        top.Children.Add(LaterButton);

        var stack = new StackPanel();
        stack.Children.Add(top);
        var pic = Picture(LandingRules.KnockArt(item));
        _art = pic != null
            ? new Border
            {
                Margin = new Thickness(0, 8, 0, 8), CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Height = (CardWidth - 20) * 9 / 16, Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill },
            }
            : new Border { Height = 8 };
        stack.Children.Add(_art);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        if (goLabel != null)
        {
            GoButton = FriendsDrawer.Pill(goLabel, FriendsDrawer.Mint, FriendsDrawer.MintInk, "knock-go", FriendsDrawer.Mint);
            GoButton.Padding = new Thickness(10, 5, 10, 5);
            ((TextBlock)GoButton.Content!).FontWeight = FontWeight.SemiBold;
            GoButton.Click += (_, _) => Fold(KnockOutcome.Go);
            buttons.Children.Add(GoButton);
        }
        NotNowButton = FriendsDrawer.Pill(notNowLabel, Brushes.Transparent, FriendsDrawer.Muted, "knock-not-now", FriendsDrawer.Line2);
        NotNowButton.Padding = new Thickness(10, 5, 10, 5);
        NotNowButton.Margin = new Thickness(GoButton != null ? 6 : 0, 0, 0, 0);
        NotNowButton.Click += (_, _) => Fold(KnockOutcome.NotNow);
        buttons.Children.Add(NotNowButton);
        stack.Children.Add(buttons);

        _card = new Border
        {
            Width = CardWidth, Background = Ground, BorderBrush = FriendsDrawer.Line2, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 10, 10, 9),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #8C000000"),   // WPF DropShadow blur 24, depth 8, 0.55
            Child = stack, RenderTransform = _shift, Tag = "knock-card", VerticalAlignment = VerticalAlignment.Top,
        };
        Content = _card;

        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => Advance(Clock());
        // Escape puts it away without answering; only Not now tells the sender no (WPF :168).
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) Fold(KnockOutcome.Later); }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerEntered += (_, _) => { _hovered = true; ApplyTuck(); };
        PointerExited += (_, _) => { _hovered = false; ApplyTuck(); };
        SizeChanged += (_, _) => Restack();
        UpdateCountdown(now);
    }

    private static Bitmap? Picture(string resourcePath)
    {
        try { using var s = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + resourcePath)); return Bitmap.DecodeToWidth(s, 480); }
        catch (Exception ex) { Log.Debug("[Friends] picture {P}: {E}", resourcePath, ex.Message); return null; }
    }

    private Control BuildRing()
    {
        var grid = new Grid { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Ellipse { Width = RingSize, Height = RingSize, Stroke = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), StrokeThickness = 4 });
        _arc.Stroke = FriendsDrawer.Lilac;
        _arc.StrokeThickness = 4;
        _arc.StrokeLineCap = PenLineCap.Round;
        grid.Children.Add(_arc);
        DrawArc(LandingRules.RingFraction(_start, _end, Clock()));
        return grid;
    }

    /// <summary>The lit part of the ring, clockwise from 12 o'clock (WPF :224-240).</summary>
    private void DrawArc(double fraction)
    {
        const double r = (RingSize - 4) / 2, c = RingSize / 2;
        if (fraction <= 0) { _arc.Data = null; return; }
        if (fraction >= 0.999) { _arc.Data = new EllipseGeometry(new Rect(c - r, c - r, 2 * r, 2 * r)); return; }
        var (x, y) = LandingRules.RingPoint(c, c, r, fraction);
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false, IsFilled = false };
        fig.Segments!.Add(new ArcSegment { Point = new Point(x, y), Size = new Size(r, r), IsLargeArc = fraction > 0.5, SweepDirection = SweepDirection.Clockwise });
        _arc.Data = new PathGeometry { Figures = new PathFigures { fig } };
    }

    private void UpdateCountdown(DateTimeOffset now)
    {
        var s = LandingRules.SecondsLeft(_end, now);
        if (s == _lastSecond) return;
        _lastSecond = s;
        Countdown.Text = LandingRules.Countdown(_end, now);
        // The last half minute reads warmer (WPF :250).
        Countdown.Foreground = s <= 30 ? FriendsDrawer.Gold : FriendsDrawer.Muted;
    }

    /// <summary>One clock step (WPF Advance :253): ring, countdown, the tuck after the ring time, run out.</summary>
    internal void Advance(DateTimeOffset now)
    {
        var f = LandingRules.RingFraction(_start, _end, now);
        DrawArc(f);
        UpdateCountdown(now);
        if (!_tucked && LandingRules.KnockTucked(_shownAt, now)) { _tucked = true; ApplyTuck(); }
        if (f <= 0) Fold(KnockOutcome.RanOut);
    }

    /// <summary>Tucked and not hovered: the small strip. Otherwise the whole card (WPF :264).</summary>
    private void ApplyTuck()
    {
        bool small = IsTucked;
        _art.IsVisible = !small;
        _line.IsVisible = !small;
        _card.Width = small ? TuckedWidth : CardWidth;
        _card.Opacity = small ? 0.9 : 1;
    }

    internal void Fold(KnockOutcome outcome)
    {
        if (_folded) return;
        _folded = true;
        _tick.Stop();
        Open.Remove(this);
        Restack();
        try { _done(Item, outcome); }
        catch (Exception ex) { Log.Warning(ex, "[Friends] knock {Outcome} failed", outcome); }
        if (AmbientFxCanvas.Env.Level == MotionLevel.Off) { SafeClose(); return; }
        // WPF :282-286: 180 ms fade while it lifts 6 px, then it closes.
        _foldFrom = _card.Opacity;
        Animate(1);
    }

    private double _foldFrom = 1;

    private void Animate(int which)
    {
        _anim = which;
        _animStart = null;
        RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan t)
    {
        if (_anim < 0) return;
        _animStart ??= t;
        var ms = (t - _animStart.Value).TotalMilliseconds;
        if (_anim == 0)
        {
            // WPF Enter :297-301: y -6 -> 0 BackEase(0.7) out over 250 ms (125 Reduced), opacity 0 -> 1 over 200 ms.
            var dur = AmbientFxCanvas.Env.Level == MotionLevel.Full ? 250 : 125;
            _shift.Y = -6 + 6 * FloatingWord.BackOut(Math.Min(1, ms / dur), 0.7);
            _card.Opacity = Math.Min(1, ms / 200) * (IsTucked ? 0.9 : 1);
            if (ms >= Math.Max(dur, 200)) { _anim = -1; _card.Opacity = IsTucked ? 0.9 : 1; return; }
        }
        else
        {
            var p = Math.Min(1, ms / 180);
            _card.Opacity = _foldFrom * (1 - p);
            _shift.Y = -6 * p;
            if (p >= 1) { _anim = -1; SafeClose(); return; }
        }
        RequestAnimationFrame(OnFrame);
    }

    private void SafeClose()
    {
        try { Close(); } catch { /* already gone */ }
    }

    private void Enter()
    {
        _shownAt = Clock();
        if (AmbientFxCanvas.Env.Level != MotionLevel.Off) { _card.Opacity = 0; Animate(0); }
        _tick.Start();
        // On screen now: that is what "seen" means on the wire (WPF :304).
        try { _shown?.Invoke(Item); }
        catch (Exception ex) { Log.Debug("[Friends] knock seen: {E}", ex.Message); }
    }

    /// <summary>Cards stack down the anchor's top-right corner, newest at the bottom (WPF :310-325):
    /// right edge 14 in, first card 34 below the top.</summary>
    private static void Restack()
    {
        double y = 0;
        foreach (var card in Open)
        {
            try
            {
                if (card._anchor == null) continue;
                var k = card._anchor.DesktopScaling > 0 ? card._anchor.DesktopScaling : 1;
                var size = card._anchor.ClientSize;
                var w = card.ClientSize.Width > 0 ? card.ClientSize.Width : CardWidth;
                card.Position = card._anchor.Position + new PixelPoint((int)Math.Round((size.Width - w - 14) * k), (int)Math.Round((34 + y) * k));
                y += card.ClientSize.Height + Gap;
            }
            catch (Exception ex) { Log.Debug("[Friends] knock restack: {E}", ex.Message); }
        }
    }

    internal static IReadOnlyList<KnockCard> Up => Open;

    /// <summary>Puts a card up for <paramref name="item"/> unless one is already up for it (WPF Show :330).</summary>
    public static KnockCard? Show(Window? anchor, InboxItem item, string line, string? goLabel, string notNowLabel, string laterTip,
        Action<InboxItem, KnockOutcome> done, Action<InboxItem>? shown = null)
    {
        if (anchor == null || item == null) return null;
        foreach (var c in Open) if (c.Item.Id == item.Id) return null;
        try
        {
            var card = new KnockCard(anchor, item, line, goLabel, notNowLabel, laterTip, done, shown);
            card.Position = new PixelPoint(-10000, -10000);
            // Closed from outside (its owner went): no outcome, just out of the stack with its clock stopped.
            card.Closed += (_, _) =>
            {
                card._folded = true;
                card._tick.Stop();
                if (Open.Remove(card)) Restack();
            };
            Open.Add(card);
            if (anchor.IsVisible) card.Show(anchor); else card.Show();
            Restack();
            card.Enter();
            return card;
        }
        catch (Exception ex) { Log.Warning(ex, "[Friends] knock card failed"); return null; }
    }

    public static bool AnyUp => Open.Count > 0;

    /// <summary>A hold started (or a panic): every card goes away unanswered, as Later, so it waits in the Inbox.</summary>
    public static void FoldAll()
    {
        foreach (var c in Open.ToArray()) c.Fold(KnockOutcome.Later);
    }

    /// <summary>Exit path: close every card without running its outcome.</summary>
    public static void CloseAll()
    {
        var all = Open.ToArray();
        Open.Clear();
        foreach (var c in all)
        {
            c._folded = true;
            c._tick.Stop();
            c.SafeClose();
        }
    }
}
