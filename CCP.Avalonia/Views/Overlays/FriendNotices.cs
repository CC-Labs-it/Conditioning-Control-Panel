using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>How one corner notice reads and what its parts do (WPF FriendNotices.cs:20). Replaced on a
/// fold, so the words and the action always belong to the newest arrival.</summary>
internal sealed class NoticeLook
{
    public string Line = "";
    /// <summary>A poke's word, drawn after the line in pink or mint. Null for the rest.</summary>
    public string? Word;
    public bool Pink;
    public string? ActionLabel;
    public Action<object?>? Act;
    /// <summary>A click on the body: the panel and the drawer on that friend.</summary>
    public Action<object?>? Open;
    /// <summary>The notice left without its action (ran out, closed, clicked).</summary>
    public Action<object?>? Left;
    /// <summary>The notice went unseen: ran out, pushed off a full stack, or a hold folded it.</summary>
    public Action<object?>? Missed;
}

/// <summary>
/// PORTED from ConditioningControlPanel/Windows/Friends/FriendNotices.cs: the corner notifications. One small
/// window that never activates, in the bottom-right of the work area of the anchor's screen, holding up to three
/// notices newest on top and "+N more" under them. The stack rules are Core's <see cref="FriendNoticeStack"/>; this
/// draws them, runs the lifetime bars, pauses on hover and closes itself when the last notice leaves.
/// Unowned like WPF's, so it never holds the panel open; FriendsLanding.Stop closes it on exit.
/// </summary>
internal sealed class FriendNotices : Window
{
    private const double CardWidth = 300;
    // WPF's 50 ms lifetime clock (bars + timers), not an ambient render loop: it stops with the window.
    private const int TickMs = 50;
    private static readonly IBrush Ground = new SolidColorBrush(Color.FromArgb(0xF2, 0x22, 0x16, 0x41));   // WPF Ground 0xF2
    private static readonly IBrush BarLilac = new SolidColorBrush(Color.FromArgb(0xCC, 0xB9, 0x9C, 0xFF));
    private static readonly IBrush BarPink = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x5F, 0xB4));

    private static FriendNotices? _current;

    private readonly FriendNoticeStack _stack = new();
    private readonly Dictionary<FriendNotice, NoticeLook> _looks = new();
    private readonly Dictionary<FriendNotice, (Border Card, ScaleTransform Bar, TextBlock Ago)> _drawn = new();
    private readonly StackPanel _column = new();
    private readonly DispatcherTimer _tick;
    private Window? _anchor;
    private FriendNotice? _fresh;
    private bool _freshFolded;
    private DateTime _last = DateTime.UtcNow;
    private int _agoTick;

    /// <summary>--render-all only: one pink poke with its Poke back.</summary>
    internal FriendNotices() : this(sample: true) { }

    private FriendNotices(bool sample)
    {
        // WPF LandingChrome.Dress + PassiveToastWindow: borderless, transparent, no taskbar, never activated, topmost.
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _column;
        PointerEntered += (_, _) => _stack.Paused = true;
        PointerExited += (_, _) => { _stack.Paused = false; _last = DateTime.UtcNow; };
        SizeChanged += (_, _) => Place();
        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _tick.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _last).TotalMilliseconds;
            _last = now;
            Step(elapsed);
        };
        // Closed by anyone else (a shutdown sweep): never leave a dead window as the one Show reuses.
        Closed += (_, _) =>
        {
            _tick.Stop();
            if (ReferenceEquals(_current, this)) _current = null;
        };
        if (!sample) return;
        var n = new FriendNotice(NoticeKind.Poke, "f", "Bambi", DateTimeOffset.UtcNow, FriendNoticeRules.LifetimeMs(NoticeKind.Poke));
        _stack.Add(n);
        _looks[n] = new NoticeLook { Line = Loc.Get("friends_notice_poked"), Word = Loc.Get("friends_poke_tut"), Pink = true, ActionLabel = Loc.Get("friends_notice_poke_back"), Act = _ => { } };
        Rebuild();
    }

    /// <summary>The window up now, if any (tests).</summary>
    internal static FriendNotices? Current => _current;
    internal IReadOnlyList<FriendNotice> Notices => _stack.All;
    internal StackPanel Column => _column;

    /// <summary>Puts a notice up (or folds it into a live one). Never throws (WPF Show :86).</summary>
    public static void Show(Window? anchor, FriendNotice notice, NoticeLook look)
    {
        try
        {
            var w = _current;
            if (w == null)
            {
                w = new FriendNotices(sample: false);
                _current = w;
                w.Position = new PixelPoint(-10000, -10000);
            }
            if (anchor != null) w._anchor = anchor;
            var (up, folded) = w._stack.Add(notice);
            foreach (var n in w._stack.TakeEvicted()) w.Leave(n, acted: false, missed: true);
            foreach (var n in new List<FriendNotice>(w._looks.Keys))
                if (!Contains(w._stack.All, n)) w._looks.Remove(n);
            w._looks[up] = look;
            w._fresh = up;
            w._freshFolded = folded;
            // Shown before drawing: the enter tween runs on this window's render clock.
            if (!w.IsVisible) { w.Show(); w._last = DateTime.UtcNow; w._tick.Start(); }
            w.Rebuild();
            w.Place();
        }
        catch (Exception ex) { Log.Debug("[Friends] notice: {E}", ex.Message); }
    }

    /// <summary>Everything down at once (WPF :115). The exit path passes no fold; a hold starting folds,
    /// so a knock that was up still waits in the Inbox.</summary>
    public static void CloseAll(bool fold = false)
    {
        var w = _current;
        _current = null;
        if (w == null) return;
        try
        {
            w._tick.Stop();
            if (fold) foreach (var n in new List<FriendNotice>(w._stack.All)) w.Leave(n, acted: false, missed: true);
            w._stack.Clear();
            w.Close();
        }
        catch { /* already gone */ }
    }

    public static bool AnyUp => _current?._stack.All.Count > 0;

    private static bool Contains(IReadOnlyList<FriendNotice> list, FriendNotice n)
    {
        foreach (var x in list) if (ReferenceEquals(x, n)) return true;
        return false;
    }

    // ---------------------------------------------------------------- timers

    /// <summary>One clock step of <paramref name="elapsedMs"/> real time (WPF OnTick :138); tests step it.</summary>
    internal void Step(double elapsedMs)
    {
        var gone = _stack.Tick(elapsedMs);
        foreach (var n in _stack.Visible)
            if (_drawn.TryGetValue(n, out var d)) d.Bar.ScaleX = n.Fraction;
        if (++_agoTick >= 1000 / TickMs)   // the relative time re-reads once a second
        {
            _agoTick = 0;
            foreach (var n in _stack.Visible)
                if (_drawn.TryGetValue(n, out var d)) d.Ago.Text = AgoText(n);
        }
        if (gone.Count == 0) return;
        foreach (var n in gone) Leave(n, acted: false, missed: true);
        AfterRemoval();
    }

    private void Leave(FriendNotice n, bool acted, bool missed = false)
    {
        if (!_looks.TryGetValue(n, out var look)) return;
        _looks.Remove(n);
        if (acted) return;
        try { look.Left?.Invoke(n.Payload); }
        catch (Exception ex) { Log.Debug("[Friends] notice left: {E}", ex.Message); }
        if (!missed) return;
        try { look.Missed?.Invoke(n.Payload); }
        catch (Exception ex) { Log.Debug("[Friends] notice missed: {E}", ex.Message); }
    }

    /// <summary>Takes one notice down after a press, with a quick fade where motion allows (WPF :172).</summary>
    private void Dismiss(FriendNotice n, bool acted)
    {
        if (!_stack.Remove(n)) return;
        Leave(n, acted);
        if (AmbientFxCanvas.Env.Level != MotionLevel.Off && _drawn.TryGetValue(n, out var d))
        {
            var from = d.Card.Opacity;
            Animate(d.Card, 140, p => d.Card.Opacity = from * (1 - p), AfterRemoval);
            return;
        }
        AfterRemoval();
    }

    private void AfterRemoval()
    {
        if (_stack.All.Count == 0)
        {
            if (ReferenceEquals(_current, this)) _current = null;
            try { _tick.Stop(); Close(); } catch { /* already gone */ }
            return;
        }
        _fresh = null;
        Rebuild();
    }

    // ---------------------------------------------------------------- placement

    /// <summary>Bottom-right of the anchor's screen's work area, above the taskbar (WPF Place :201), in device pixels.</summary>
    private void Place()
    {
        try
        {
            if (!IsVisible) return;
            var screen = (_anchor is { IsVisible: true } a ? Screens.ScreenFromVisual(a) : null) ?? Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            var s = screen.Scaling > 0 ? screen.Scaling : 1;
            var (left, top) = FriendNoticeRules.Place(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s, Bounds.Width, Bounds.Height);
            Position = new PixelPoint((int)Math.Round(left * s), (int)Math.Round(top * s));
        }
        catch (Exception ex) { Log.Debug("[Friends] notice place: {E}", ex.Message); }
    }

    // ---------------------------------------------------------------- drawing

    private void Rebuild()
    {
        _column.Children.Clear();
        _drawn.Clear();
        foreach (var n in _stack.Visible)
        {
            if (!_looks.TryGetValue(n, out var look)) continue;
            var card = BuildCard(n, look, out var bar, out var ago);
            _drawn[n] = (card, bar, ago);
            _column.Children.Add(card);
            if (ReferenceEquals(n, _fresh)) Enter(card, _freshFolded);
        }
        if (_stack.Hidden > 0)
            _column.Children.Add(new TextBlock
            {
                Text = Loc.GetF("friends_notice_more", _stack.Hidden), FontFamily = FriendsDrawer.Display, FontSize = 11.5,
                Foreground = FriendsDrawer.Muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0),
                Tag = "notice-more",
            });
    }

    internal static string AgoText(FriendNotice n)
    {
        var (key, arg) = FriendNoticeRules.Ago(n.At, DateTimeOffset.UtcNow);
        return key is "friends_notice_seconds" or "friends_notice_minutes" or "friends_notice_hours" ? Loc.GetF(key, arg) : Loc.Get(key);
    }

    private Border BuildCard(FriendNotice n, NoticeLook look, out ScaleTransform bar, out TextBlock ago)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        // ponytail: initials only, like the drawer and the knock card; WPF LandingChrome.Avatar also fetched the proxy picture.
        var avatar = FriendsDrawer.Avatar(n.Name, 34, null);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(avatar);

        var words = new StackPanel { Margin = new Thickness(10, 0, 4, 0) };
        ago = new TextBlock
        {
            Text = AgoText(n), FontSize = 10.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = FriendsDrawer.Muted,
        };
        var headRow = new DockPanel();
        DockPanel.SetDock(ago, Dock.Right);
        headRow.Children.Add(ago);
        headRow.Children.Add(new TextBlock
        {
            Text = n.Name, FontFamily = FriendsDrawer.Display, FontWeight = FontWeight.SemiBold, FontSize = 13.5,
            Foreground = FriendsDrawer.Text, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(headRow);

        var line = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0), Foreground = FriendsDrawer.Muted,
            Tag = "notice-line",
        };
        line.Inlines!.Add(new Run(look.Line));
        if (!string.IsNullOrEmpty(look.Word))
            line.Inlines.Add(new Run(" " + look.Word) { FontWeight = FontWeight.SemiBold, Foreground = look.Pink ? FriendsDrawer.Pink : FriendsDrawer.Mint });
        if (n.Count > 1)
            line.Inlines.Add(new Run(FriendNoticeRules.CountSuffix(n.Count)) { FontWeight = FontWeight.SemiBold, Foreground = FriendsDrawer.Gold });
        words.Children.Add(line);

        if (look.ActionLabel != null && look.Act != null)
        {
            var act = FriendsDrawer.Pill(look.ActionLabel, FriendsDrawer.Mint, new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x22)), "notice-act");
            ((TextBlock)act.Content!).FontWeight = FontWeight.SemiBold;
            (act.Padding, act.BorderThickness, act.HorizontalAlignment, act.Margin) = (new Thickness(12, 4, 12, 4), new Thickness(0), HorizontalAlignment.Left, new Thickness(0, 7, 0, 0));
            act.Cursor = FriendsDrawer.Hand();
            act.Click += (_, _) =>
            {
                Dismiss(n, acted: true);
                try { look.Act(n.Payload); }
                catch (Exception ex) { Log.Debug("[Friends] notice action: {E}", ex.Message); }
            };
            words.Children.Add(act);
        }
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        // WPF: the MDL2 close glyph (E711), a Fluent Dismiss here.
        var tip = Loc.Get("friends_notice_close");
        var close = FriendsDrawer.Pill(new IconGlyph { Kind = IconKind.Dismiss, Size = 9 }, Brushes.Transparent, FriendsDrawer.Muted, "notice-close");
        (close.Padding, close.BorderThickness, close.VerticalAlignment) = (new Thickness(5), new Thickness(0), VerticalAlignment.Top);
        close.Cursor = FriendsDrawer.Hand();
        ToolTip.SetTip(close, tip);
        global::Avalonia.Automation.AutomationProperties.SetName(close, tip);
        close.Click += (_, _) => Dismiss(n, acted: false);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        bar = new ScaleTransform(n.Fraction, 1);
        var barLine = new Border
        {
            Height = 2, Margin = new Thickness(0, 9, 0, 0), CornerRadius = new CornerRadius(1),
            Background = n.Kind == NoticeKind.Poke && look.Pink ? BarPink : BarLilac,
            // Avalonia's origin defaults to the centre; WPF's bar shrinks toward its left end.
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = bar, Tag = "notice-bar",
        };

        var card = new Border
        {
            Width = CardWidth, Background = Ground, BorderBrush = FriendsDrawer.Line2, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 10, 9, 8), Margin = new Thickness(16, 0, 0, 8),
            Cursor = FriendsDrawer.Hand(), Child = new StackPanel { Children = { grid, barLine } },
            RenderTransform = new TranslateTransform(), Tag = "friend-notice",
            BoxShadow = BoxShadows.Parse("0 5 18 0 #80000000"),   // WPF DropShadow blur 18, depth 5, 0.5
        };
        // A Button marks its own release handled, so this only fires for a click on the body (WPF :384).
        card.PointerReleased += (_, e) =>
        {
            if (e.Handled || e.InitialPressMouseButton != MouseButton.Left) return;
            Dismiss(n, acted: false);
            try { look.Open?.Invoke(n.Payload); }
            catch (Exception ex) { Log.Debug("[Friends] notice open: {E}", ex.Message); }
        };
        return card;
    }

    /// <summary>WPF Enter :393: a new notice slides in from 28 px right (BackEase 0.4, 260 ms) and fades in
    /// (200 ms; Reduced 140 ms fade only); a repeat nudges from -6 (BackEase 0.6, 220 ms, Full only).</summary>
    private void Enter(Border card, bool folded)
    {
        var level = AmbientFxCanvas.Env.Level;
        if (level == MotionLevel.Off) return;
        var full = level == MotionLevel.Full;
        var slide = (TranslateTransform)card.RenderTransform!;
        if (folded)
        {
            if (!full) return;
            slide.X = -6;
            Animate(card, 220, p => slide.X = -6 + 6 * FloatingWord.BackOut(p, 0.6), null);
            return;
        }
        card.Opacity = 0;
        if (full)
        {
            slide.X = 28;
            Animate(card, 260, p => slide.X = 28 - 28 * FloatingWord.BackOut(p, 0.4), null);
        }
        var fade = full ? 200 : 140;
        Animate(card, fade, p => card.Opacity = p, null);
    }

    /// <summary>A per-frame tween of <paramref name="ms"/> on this window's render clock (P82: no Animation on a Transform).</summary>
    private void Animate(Border card, double ms, Action<double> apply, Action? done)
    {
        if (!IsVisible) { apply(1); done?.Invoke(); return; }
        TimeSpan? start = null;
        void Frame(TimeSpan t)
        {
            start ??= t;
            var p = Math.Min(1, (t - start.Value).TotalMilliseconds / ms);
            apply(p);
            if (p >= 1) { done?.Invoke(); return; }
            if (IsVisible) RequestAnimationFrame(Frame);
            else { apply(1); done?.Invoke(); }
        }
        RequestAnimationFrame(Frame);
    }
}
