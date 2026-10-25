using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal sealed class NoticeLook
{
    public string Line = "";
    public string? Word, ActionLabel, AvatarUrl;
    public bool Pink;
    public Action<object?>? Act, Open, Left, Missed;
}

// Adapted from WPF Windows/Friends/FriendNotices.cs. Core owns folding, caps and timers.
internal sealed class FriendNotices : Window
{
    internal static FriendNotices? Current { get; private set; }
    internal readonly FriendNoticeStack Stack = new();
    private readonly Dictionary<FriendNotice, NoticeLook> _looks = new();
    private readonly Dictionary<FriendNotice, (ScaleTransform Bar, TextBlock Ago)> _drawn = new();
    private readonly StackPanel _column = new();
    private readonly DispatcherTimer _timer;
    private Window? _anchor;
    private DateTime _last = DateTime.UtcNow;

    internal FriendNotices()
    {
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _column;
        PointerEntered += (_, _) => Stack.Paused = true;
        PointerExited += (_, _) => { Stack.Paused = false; _last = DateTime.UtcNow; };
        SizeChanged += (_, _) => Place();
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle() is { } handle)
                Win32Overlay.SetOverrideRedirect(this, handle.Handle, passive: true);
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => { var now = DateTime.UtcNow; Advance((now - _last).TotalMilliseconds); _last = now; };
        Closed += (_, _) => { _timer.Stop(); if (ReferenceEquals(Current, this)) Current = null; };
    }

    internal static void Present(Window? anchor, FriendNotice notice, NoticeLook look)
    {
        try
        {
            var w = Current ??= new FriendNotices();
            w._anchor = anchor ?? w._anchor;
            var (up, _) = w.Stack.Add(notice);
            foreach (var evicted in w.Stack.TakeEvicted()) w.Leave(evicted, false, true);
            w._looks[up] = look;
            w.Rebuild();
            if (!w.IsVisible) { w.Show(); w._last = DateTime.UtcNow; w._timer.Start(); }
            w.Place();
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Friend notice presentation failed"); }
    }

    internal static void CloseAll(bool fold = false)
    {
        var w = Current;
        if (w == null) return;
        if (fold) foreach (var n in new List<FriendNotice>(w.Stack.All)) w.Leave(n, false, true);
        w.Stack.Clear();
        w.Close();
    }

    internal void Advance(double ms)
    {
        var gone = Stack.Tick(ms);
        foreach (var n in Stack.Visible)
            if (_drawn.TryGetValue(n, out var d)) { d.Bar.ScaleX = n.Fraction; d.Ago.Text = Ago(n); }
        foreach (var n in gone) Leave(n, false, true);
        if (gone.Count > 0) AfterRemoval();
    }

    private void Leave(FriendNotice n, bool acted, bool missed)
    {
        if (!_looks.Remove(n, out var look) || acted) return;
        try { look.Left?.Invoke(n.Payload); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Friend notice left"); }
        if (missed) try { look.Missed?.Invoke(n.Payload); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Friend notice missed"); }
    }

    internal void Dismiss(FriendNotice n, bool acted, bool open = false)
    {
        if (!_looks.TryGetValue(n, out var look) || !Stack.Remove(n)) return;
        Leave(n, acted, false);
        AfterRemoval();
        try { if (acted) look.Act?.Invoke(n.Payload); else if (open) look.Open?.Invoke(n.Payload); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Friend notice action"); }
    }

    private void AfterRemoval()
    {
        if (Stack.All.Count == 0) Close(); else Rebuild();
    }

    private void Place()
    {
        if (!IsVisible) return;
        var screen = (_anchor == null ? null : Screens.ScreenFromWindow(_anchor)) ?? Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var (x, y) = FriendNoticeRules.Place(area.X / scale, area.Y / scale,
            area.Width / scale, area.Height / scale, Bounds.Width, Bounds.Height);
        Position = new PixelPoint((int)Math.Round(x * scale), (int)Math.Round(y * scale));
    }

    private static string Ago(FriendNotice n)
    {
        var (key, arg) = FriendNoticeRules.Ago(n.At, DateTimeOffset.UtcNow);
        return string.Format(Loc.Get(key), arg);
    }

    private void Rebuild()
    {
        _column.Children.Clear();
        _drawn.Clear();
        foreach (var n in Stack.Visible)
        {
            var look = _looks[n];
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            // ponytail: initials reuse the drawer avatar; proxy pictures and arrival/fade motion remain unported.
            grid.Children.Add(FriendsDrawer.Avatar(n.Name, 34, null));
            var words = new StackPanel { Margin = new Thickness(10, 0, 4, 0) };
            var ago = new TextBlock { Text = Ago(n), FontSize = 10.5, Foreground = FriendsDrawer.Muted };
            words.Children.Add(new TextBlock { Text = n.Name, FontFamily = FriendsDrawer.Display,
                FontSize = 13.5, Foreground = FriendsDrawer.Text, TextTrimming = TextTrimming.CharacterEllipsis });
            words.Children.Add(ago);
            words.Children.Add(new TextBlock { Text = look.Line + (look.Word == null ? "" : " " + look.Word)
                + FriendNoticeRules.CountSuffix(n.Count), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = look.Word == null ? FriendsDrawer.Muted : look.Pink ? FriendsDrawer.Pink : FriendsDrawer.Mint });
            if (look.ActionLabel != null && look.Act != null)
            {
                var act = FriendsDrawer.Pill(look.ActionLabel, FriendsDrawer.Mint, Brushes.Black, "friend-notice-action");
                act.Focusable = false;
                act.Margin = new Thickness(0, 7, 0, 0);
                act.Click += (_, e) => { e.Handled = true; Dismiss(n, true); };
                words.Children.Add(act);
            }
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            var close = FriendsDrawer.Pill(new IconGlyph { Kind = IconKind.Dismiss, Size = 10 },
                Brushes.Transparent, FriendsDrawer.Muted, "friend-notice-close");
            close.Focusable = false;
            close.VerticalAlignment = VerticalAlignment.Top;
            ToolTip.SetTip(close, Loc.Get("friends_notice_close"));
            close.Click += (_, e) => { e.Handled = true; Dismiss(n, false); };
            Grid.SetColumn(close, 2);
            grid.Children.Add(close);
            var bar = new ScaleTransform(n.Fraction, 1);
            var body = new StackPanel();
            body.Children.Add(grid);
            body.Children.Add(new Border { Height = 2, Margin = new Thickness(0, 9, 0, 0),
                Background = FriendsDrawer.Pink, RenderTransform = bar,
                RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative) });
            var card = new Border { Width = 300, Padding = new Thickness(11, 10, 9, 8),
                Margin = new Thickness(0, 0, 0, 8), Background = new SolidColorBrush(Color.Parse("#F2221641")),
                BorderBrush = FriendsDrawer.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Child = body };
            card.PointerReleased += (_, e) => { if (!e.Handled) Dismiss(n, false, true); };
            _column.Children.Add(card);
            _drawn[n] = (bar, ago);
        }
        if (Stack.Hidden > 0) _column.Children.Add(new TextBlock {
            Text = string.Format(Loc.Get("friends_notice_more"), Stack.Hidden), Foreground = FriendsDrawer.Muted,
            HorizontalAlignment = HorizontalAlignment.Right });
    }
}
