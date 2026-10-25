using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

/// <summary>WPF ChaosUnlockCardOverlay: clickable scrim, FIFO cards, hidden between uses.
/// Pause-holding cards wait for a click; other cards expire after twelve seconds.</summary>
internal sealed class ChaosUnlockCardOverlay : Window
{
    internal const int InMs = 160, OutMs = 150, AutoDismissMs = 12000;
    private static readonly Queue<(ChaosUnlockCardInfo Data, Action? Done, bool Auto)> Queue = new();
    private static ChaosUnlockCardOverlay? _active;
    private static bool _showing;
    private static int _generation;
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;
    internal static ChaosUnlockCardOverlay? Active => _active;
    public static bool IsShowing => _showing || Queue.Count > 0;
    internal static int QueuedCount => Queue.Count;

    private readonly Grid _host = new() { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)) };
    private readonly BeatLoop? _frame;
    private readonly DispatcherTimer _auto = new() { Interval = TimeSpan.FromMilliseconds(AutoDismissMs) };
    private ChaosUnlockCardLayer.Card? _card;
    private ScaleTransform? _scale;
    private Action? _done;
    private long _start, _dismissStart;
    private double _fadeFrom, _enterFrom;
    private bool _dismissing, _autoDismiss;
    internal ChaosUnlockCardInfo? Current { get; private set; }
    internal bool AutoDismiss => _autoDismiss;
    internal double ContentOpacity => _host.Opacity;

    public static void Show(ChaosUnlockCardInfo data, Action? onDismissed = null, bool autoDismiss = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            int generation = _generation;
            Dispatcher.UIThread.Post(() => { if (generation == _generation) Show(data, onDismissed, autoDismiss); });
            return;
        }
        Queue.Enqueue((data, onDismissed, autoDismiss));
        if (!_showing) ShowNext();
    }

    public static void CloseActive()
    {
        _generation++;
        Queue.Clear();
        _showing = false;
        var active = _active;
        _active = null;
        active?.CloseNow();   // callbacks are discarded: teardown owns the pause flags
    }

    private static void ShowNext()
    {
        if (Queue.Count == 0) { _showing = false; return; }
        var next = Queue.Dequeue();
        _showing = true;
        try
        {
            _active ??= new ChaosUnlockCardOverlay(live: true);
            if (!_active.IsVisible) _active.Show();
            _active.Display(next.Data, next.Done, next.Auto);
        }
        catch (Exception ex)
        {
            Log.Debug("Chaos unlock card: {E}", ex.Message);
            CloseActive();
            Notify(next.Done);
        }
    }

    /// <summary>--render-view sample: no timer, cue, queue or desktop access.</summary>
    internal ChaosUnlockCardOverlay() : this(live: false)
    {
        Width = 1280;
        Height = 720;
        Build(ChaosUnlockCardRules.ForLesson("taking_chances")!);
        _host.Opacity = 1;
        _scale!.ScaleX = _scale.ScaleY = 1;
        ChaosUnlockCardLayer.AnimateCard(_card!, 1000);
    }

    private ChaosUnlockCardOverlay(bool live)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = CoreSettings.Current?.ChaosPinOnTop ?? true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        Title = "CCP Chaos unlock card";
        Content = _host;
        _host.Opacity = 0;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { Dismiss(); e.Handled = true; }
        };
        _auto.Tick += (_, _) => Dismiss();   // the timer IS the 12 s expiry; an early tick must not re-arm it
        if (!live) return;
        Opened += (_, _) => SizeToScreens();
        _frame = new BeatLoop(_host, _ => Step());
        Closed += (_, _) =>
        {
            _auto.Stop();
            _frame.Stop();
            var done = _done;
            _done = null;
            Current = null;
            if (!ReferenceEquals(_active, this)) return;   // CloseActive: teardown owns the pause flags
            var queued = Queue.Select(q => q.Done).ToList();
            _active = null;
            Queue.Clear();
            _showing = false;
            Notify(done);   // closed by the window manager: release the lesson pause like a click
            foreach (var d in queued) Notify(d);
        };
    }

    private void Display(ChaosUnlockCardInfo data, Action? done, bool autoDismiss)
    {
        _auto.Stop();
        _frame?.Stop();
        Current = data;
        _done = done;
        _dismissing = false;
        _autoDismiss = autoDismiss;
        _enterFrom = _host.Opacity;
        _start = Clock.GetTimestamp();
        Build(data);
        var (cue, volume) = data.Cue;
        ChaosOverlayWindow.ChaosSfx.Play(cue, volume);
        _frame?.Start();
        if (autoDismiss) _auto.Start();
    }

    private void Build(ChaosUnlockCardInfo data)
    {
        _card = ChaosUnlockCardLayer.Build(data, 420);
        _card.Root.Opacity = 1;
        _card.Slide.Y = 0;
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = _scale = new ScaleTransform(0.9, 0.9),
        };
        panel.Children.Add(_card.Root);
        panel.Children.Add(new TextBlock
        {
            Text = "click to continue", FontSize = 12, Margin = new Thickness(0, 14, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var area = new Grid();
        if (Screens.Primary is { } primary && IsVisible)
        {
            double scale = DesktopScaling;
            area.Width = primary.WorkingArea.Width / scale;
            area.Height = primary.WorkingArea.Height / scale;
            area.HorizontalAlignment = HorizontalAlignment.Left;
            area.VerticalAlignment = VerticalAlignment.Top;
            area.Margin = new Thickness((primary.WorkingArea.X - Position.X) / scale,
                (primary.WorkingArea.Y - Position.Y) / scale, 0, 0);
        }
        area.Children.Add(panel);
        _host.Children.Clear();
        _host.Children.Add(area);
    }

    internal void Step()
    {
        if (Current == null) return;
        double elapsed = Clock.GetElapsedTime(_start).TotalMilliseconds;
        if (!_dismissing && _autoDismiss && elapsed >= AutoDismissMs) { Dismiss(); return; }
        if (_dismissing)
        {
            double fade = Clock.GetElapsedTime(_dismissStart).TotalMilliseconds / OutMs;
            _host.Opacity = _fadeFrom * Math.Clamp(1 - fade, 0, 1);
            if (fade < 1) return;
            var done = _done;
            _done = null;
            _frame?.Stop();
            Current = null;
            _host.Children.Clear();
            _showing = false;
            Hide();
            Notify(done);
            if (Queue.Count > 0) ShowNext();
            return;
        }
        _host.Opacity = _enterFrom + (1 - _enterFrom) * Math.Clamp(elapsed / InMs, 0, 1);
        _scale!.ScaleX = _scale.ScaleY = 0.9 + 0.1 * ChaosUnlockCardLayer.BackOut(Math.Clamp(elapsed / (InMs + 80), 0, 1), 0.5);
        ChaosUnlockCardLayer.AnimateCard(_card!, elapsed);
        if (elapsed >= 2 * 380) _frame?.Stop();   // entrance is finite; only the expiry timer remains
    }

    internal void Dismiss()
    {
        if (_dismissing || !_showing || Current == null) return;
        _dismissing = true;
        _auto.Stop();
        if (Queue.Count > 0)
        {
            var done = _done;
            _done = null;
            Notify(done);   // IsShowing stays true until the last queued card leaves
            if (ReferenceEquals(_active, this)) ShowNext();
            return;
        }
        _dismissStart = Clock.GetTimestamp();
        _fadeFrom = _host.Opacity;
        _frame?.Start();
    }

    private void SizeToScreens()
    {
        var screens = Screens.All;
        if (screens.Count == 0) return;
        int left = screens.Min(s => s.Bounds.X), top = screens.Min(s => s.Bounds.Y);
        Position = new PixelPoint(left, top);
        Width = (screens.Max(s => s.Bounds.Right) - left) / DesktopScaling;
        Height = (screens.Max(s => s.Bounds.Bottom) - top) / DesktopScaling;
    }

    private static void Notify(Action? done)
    {
        try { done?.Invoke(); }
        catch (Exception ex) { Log.Debug("Chaos unlock card callback: {E}", ex.Message); }
    }

    private void CloseNow()
    {
        _auto.Stop();
        _frame?.Stop();
        _done = null;
        Current = null;
        _host.Children.Clear();
        Close();
    }
}
