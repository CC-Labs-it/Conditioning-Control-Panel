using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Documents;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;
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
    private readonly Dictionary<FriendNotice, (Action<object?> Act, Action<object?> Missed, Action<object?>? Open)> _actions = new();
    private readonly StackPanel _column = new();
    private readonly Dictionary<FriendNotice, (ProgressBar Bar, TextBlock Ago)> _drawn = new();
    private readonly global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop _timer;
    private Window? _anchor;
    private long _last;

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
        _timer = new global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop(this, _ => { var now = global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time.GetTimestamp(); Tick(global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time.GetElapsedTime(_last, now).TotalMilliseconds); _last = now; });
        Closed += (_, _) => { _timer.Stop(); if (ReferenceEquals(_current, this)) _current = null; };
    }

    internal static bool AnyUp => _current?._stack.All.Count > 0;
    internal static FriendNotices? Current => _current;
    internal IReadOnlyList<FriendNotice> Notices => _stack.All;
    internal int Hidden => _stack.Hidden;

    internal static void ShowPoke(Window? anchor, FriendNotice notice, Action<object?> act, Action<object?> missed, Action<object?>? open = null)
    {
        var w = _current ??= new FriendNotices();
        w._anchor = anchor ?? w._anchor;
        var (up, folded) = w._stack.Add(notice);
        foreach (var evicted in w._stack.TakeEvicted()) w.Leave(evicted, true);
        w._actions[up] = (act, missed, open);
        w.Draw();
        if (!w.IsVisible) { w.Show(); w._last = global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time.GetTimestamp(); w._timer.Start(); }
        w.Enter(up, folded);
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

    internal void Pause(bool paused) { _stack.Paused = paused; _last = global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time.GetTimestamp(); }
    internal void Tick(double elapsed)
    {
        var gone = _stack.Tick(elapsed);
        foreach (var n in gone) Leave(n, true);
        foreach (var n in _stack.Visible)
            if (_drawn.TryGetValue(n, out var d)) { d.Bar.Value = n.Fraction; d.Ago.Text = Ago(n); }
        if (gone.Count != 0) AfterRemoval();
    }

    internal void Dismiss(FriendNotice n, bool act)
    {
        if (!_stack.Remove(n)) return;
        if (act && _actions.TryGetValue(n, out var actions)) actions.Act(n.Payload);
        Leave(n, false);
        AfterPress(n);
    }

    internal void Open(FriendNotice n)
    {
        if (!_stack.Remove(n)) return;
        if (_actions.TryGetValue(n, out var actions)) actions.Open?.Invoke(n.Payload);
        Leave(n, false);
        AfterPress(n);
    }

    private void AfterPress(FriendNotice n)
    {
        if (AmbientFxCanvas.Env.Level != MotionLevel.Off &&
            _column.Children.OfType<Border>().FirstOrDefault(c => ReferenceEquals(c.Tag, n)) is { } card)
        {
            card.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) } };
            card.Opacity = 0;
            DispatcherTimer.RunOnce(() => { if (ReferenceEquals(_current, this)) AfterRemoval(); }, TimeSpan.FromMilliseconds(140));
        }
        else AfterRemoval();
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
        _drawn.Clear();
        foreach (var n in _stack.Visible)
        {
            var body = new StackPanel();
            var item = n.Payload as InboxItem;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(Avatar(n.Name, item?.FromAvatarUrl));
            body.Margin = new Thickness(10, 0, 4, 0); Grid.SetColumn(body, 1); grid.Children.Add(body);
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            head.Children.Add(new TextBlock { Text = n.Name, FontFamily = FriendsDrawer.Display, FontSize = 13.5, Foreground = FriendsDrawer.Text, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "notice-name" });
            var ago = new TextBlock { Text = Ago(n), Tag = "notice-ago", FontSize = 10.5, Margin = new Thickness(6, 0, 0, 0), Foreground = FriendsDrawer.Muted };
            Grid.SetColumn(ago, 1); head.Children.Add(ago); body.Children.Add(head);
            var word = FriendsLanding.Str("friends_poke_" + item?.PokeId, LandingRules.PokeFallback(item?.PokeId));
            var line = new TextBlock { FontSize = 12, Margin = new Thickness(0, 1, 0, 0), Foreground = FriendsDrawer.Muted, TextWrapping = TextWrapping.Wrap, Tag = "notice-line" };
            line.Inlines!.Add(new Run(Loc.Get("friends_notice_poked") + " "));
            line.Inlines.Add(new Run(word) { FontWeight = FontWeight.SemiBold, Foreground = LandingRules.PokeIsPink(item?.PokeId) ? FriendsDrawer.Pink : FriendsDrawer.Mint });
            line.Inlines.Add(new Run(FriendNoticeRules.CountSuffix(n.Count)) { FontWeight = FontWeight.SemiBold, Foreground = FriendsDrawer.Gold }); body.Children.Add(line);
            var back = FriendsDrawer.Pill(Loc.Get("friends_notice_poke_back"), FriendsDrawer.Mint, new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x22)), "notice-back"); back.Focusable = false; back.HorizontalAlignment = HorizontalAlignment.Left; back.FontWeight = FontWeight.SemiBold; back.Padding = new Thickness(12, 4); back.Margin = new Thickness(0, 7, 0, 0);
            back.Click += (_, _) => Dismiss(n, true);
            var close = FriendsDrawer.Pill(new IconGlyph { Kind = IconKind.Dismiss, Size = 10 }, Brushes.Transparent, FriendsDrawer.Muted, "notice-close");
            close.Focusable = false; close.Padding = new Thickness(5); close.VerticalAlignment = VerticalAlignment.Top; ToolTip.SetTip(close, Loc.Get("friends_notice_close"));
            close.Click += (_, _) => Dismiss(n, false); Grid.SetColumn(close, 2); grid.Children.Add(close); body.Children.Add(back);
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = n.Fraction, Height = 2, Margin = new Thickness(0, 9, 0, 0), Foreground = LandingRules.PokeIsPink(item?.PokeId) ? FriendsDrawer.Pink : FriendsDrawer.Lilac, Tag = "notice-bar" };
            var content = new StackPanel(); content.Children.Add(grid); content.Children.Add(bar);
            _drawn[n] = (bar, ago);
            var card = new Border { Child = content, Tag = n, Background = new SolidColorBrush(Color.Parse("#F2221641")), BorderBrush = FriendsDrawer.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 10, 9, 8), Margin = new Thickness(0, 0, 0, 8) };
            card.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { Open(n); e.Handled = true; } };
            _column.Children.Add(card);
        }
        if (_stack.Hidden > 0) _column.Children.Add(new TextBlock { Text = string.Format(Loc.Get("friends_notice_more"), _stack.Hidden), HorizontalAlignment = HorizontalAlignment.Right });
    }
    private static string Ago(FriendNotice n)
    {
        var (key, arg) = FriendNoticeRules.Ago(n.At, DateTimeOffset.UtcNow);
        var text = Loc.Get(key);
        return key == "friends_notice_now" ? text : string.Format(text, arg);
    }

    private static Control Avatar(string name, string? path)
    {
        var grid = new Grid { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top, Tag = "notice-avatar" };
        grid.Children.Add(FriendsDrawer.Avatar(name, 34, null));
        if (!string.IsNullOrEmpty(path) && path.StartsWith('/'))
        {
            var face = new Ellipse { Width = 34, Height = 34 };
            grid.Children.Add(face);
            _ = LoadAvatar(face, ProviderSubscription.ProxyBaseUrl + path);
        }
        return grid;
    }

    private static async System.Threading.Tasks.Task LoadAvatar(Ellipse face, string url)
    {
        try
        {
            var bytes = await KnockCard.FetchPicture(url);
            using var stream = new System.IO.MemoryStream(bytes);
            face.Fill = new ImageBrush(Bitmap.DecodeToWidth(stream, 68)) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] notice avatar: {E}", ex.Message); }
    }

    private void Enter(FriendNotice notice, bool folded)
    {
        var level = AmbientFxCanvas.Env.Level;
        if (level == MotionLevel.Off || (folded && level != MotionLevel.Full)) return;
        if (_column.Children.OfType<Border>().FirstOrDefault(c => ReferenceEquals(c.Tag, notice)) is not { } card) return;
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            if (!_stack.All.Contains(notice)) return;
            start ??= now;
            var ms = (now - start.Value).TotalMilliseconds;
            var duration = folded ? 220 : level == MotionLevel.Full ? 260 : 140;
            var t = Math.Min(1, ms / duration);
            shift.X = (folded ? -6 : level == MotionLevel.Full ? 28 : 0) * (1 - FloatingWord.BackOut(t, 0.4));
            card.Opacity = folded ? 1 : Math.Min(1, ms / (level == MotionLevel.Full ? 200 : 140));
            if (t < 1 && IsVisible) RequestAnimationFrame(Frame);
        }
        shift.X = folded ? -6 : level == MotionLevel.Full ? 28 : 0;
        if (!folded) card.Opacity = 0;
        RequestAnimationFrame(Frame);
    }

}
