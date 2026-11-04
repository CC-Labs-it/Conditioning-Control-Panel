using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// WPF Windows/Friends/FriendNotices.cs:48. A single passive corner stack for poke notices.
internal sealed class FriendNotices : Window
{
    private static FriendNotices? _current;
    private readonly FriendNoticeStack _stack = new();
    private readonly Dictionary<FriendNotice, (Action<object?> Act, Action<object?> Missed)> _actions = new();
    private readonly StackPanel _column = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Window? _anchor;
    private DateTime _last = DateTime.UtcNow;

    internal FriendNotices()
    {
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Content = _column;
        PointerEntered += (_, _) => Pause(true);
        PointerExited += (_, _) => Pause(false);
        SizeChanged += (_, _) => Place();
        _timer.Tick += (_, _) => { var now = DateTime.UtcNow; Tick((now - _last).TotalMilliseconds); _last = now; };
        Closed += (_, _) => { _timer.Stop(); if (ReferenceEquals(_current, this)) _current = null; };
    }

    internal static bool AnyUp => _current?._stack.All.Count > 0;
    internal static FriendNotices? Current => _current;
    internal IReadOnlyList<FriendNotice> Notices => _stack.All;
    internal int Hidden => _stack.Hidden;

    internal static void ShowPoke(Window? anchor, FriendNotice notice, Action<object?> act, Action<object?> missed)
    {
        var w = _current ??= new FriendNotices();
        w._anchor = anchor ?? w._anchor;
        var (up, _) = w._stack.Add(notice);
        foreach (var evicted in w._stack.TakeEvicted()) w.Leave(evicted, true);
        w._actions[up] = (act, missed);
        w.Draw();
        if (!w.IsVisible) { w.Show(); w._last = DateTime.UtcNow; w._timer.Start(); }
        w.Place();
    }

    internal static void CloseAll(bool fold = false)
    {
        var w = _current;
        if (w == null) return;
        _current = null;
        if (fold) foreach (var n in new List<FriendNotice>(w._stack.All)) w.Leave(n, true);
        w._stack.Clear();
        w.Close();
    }

    internal void Pause(bool paused) { _stack.Paused = paused; _last = DateTime.UtcNow; }
    internal void Tick(double elapsed)
    {
        var gone = _stack.Tick(elapsed);
        foreach (var n in gone) Leave(n, true);
        if (gone.Count != 0) AfterRemoval();
    }

    internal void Dismiss(FriendNotice n, bool act)
    {
        if (!_stack.Remove(n)) return;
        if (act && _actions.TryGetValue(n, out var actions)) actions.Act(n.Payload);
        Leave(n, false);
        AfterRemoval();
    }

    private void Leave(FriendNotice n, bool missed)
    {
        if (!_actions.Remove(n, out var actions)) return;
        if (missed) actions.Missed(n.Payload);
    }

    private void AfterRemoval()
    {
        if (_stack.All.Count == 0) { CloseAll(); return; }
        Draw();
        Place();
    }

    private void Place()
    {
        var screen = (_anchor == null ? null : Screens?.ScreenFromWindow(_anchor)) ?? Screens?.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var (x, y) = FriendNoticeRules.Place(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale, Bounds.Width, Bounds.Height);
        Position = new PixelPoint((int)Math.Round(x * scale), (int)Math.Round(y * scale));
    }

    private void Draw()
    {
        _column.Children.Clear();
        foreach (var n in _stack.Visible)
        {
            var body = new StackPanel { Spacing = 5 };
            body.Children.Add(new TextBlock { Text = n.Name, FontWeight = FontWeight.SemiBold });
            body.Children.Add(new TextBlock { Text = Loc.Get("friends_notice_poked") + " " + (n.Payload is InboxItem item ? Loc.Get("friends_poke_" + item.PokeId) : "") + FriendNoticeRules.CountSuffix(n.Count), TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var back = new Button { Content = Loc.Get("friends_notice_poke_back"), Focusable = false };
            back.Click += (_, _) => Dismiss(n, true);
            var close = new Button { Content = Loc.Get("friends_notice_close"), Focusable = false };
            close.Click += (_, _) => Dismiss(n, false);
            buttons.Children.Add(back); buttons.Children.Add(close); body.Children.Add(buttons);
            _column.Children.Add(new Border { Child = body, Background = new SolidColorBrush(Color.Parse("#F32B2035")), BorderBrush = Brushes.Plum, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) });
        }
        if (_stack.Hidden > 0) _column.Children.Add(new TextBlock { Text = string.Format(Loc.Get("friends_notice_more"), _stack.Hidden), HorizontalAlignment = HorizontalAlignment.Right });
    }
}
