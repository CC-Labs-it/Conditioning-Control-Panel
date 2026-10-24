using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

/// <summary>WPF ChaosUnlockCardOverlay: one kept-alive, clickable scrim; pause-holding
/// cards wait for the player, pause-free cards time out. All calls run on the UI thread.</summary>
internal sealed class ChaosUnlockCardOverlay : Window
{
    internal const double InMs = 160, PopMs = 240, OutMs = 150, AutoMs = 12000;
    private static readonly Queue<(ChaosUnlockCardInfo Info, Action? Done, bool Auto)> Queue = new();
    internal static ChaosUnlockCardOverlay? Active { get; private set; }
    internal static bool IsShowing => Active?._card != null || Queue.Count > 0;
    private readonly Grid _host = new() { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)) };
    private readonly BeatLoop _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    internal Func<double> Now;
    private ChaosUnlockCardLayer.Card? _card;
    private Action? _done;
    private bool _auto, _dismissing;
    private double _start, _fadeStart, _fadeOpacity;
    private ScaleTransform? _scale;

    private ChaosUnlockCardOverlay()
    {
        Now = () => _clock.Elapsed.TotalMilliseconds;
        SystemDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = CoreSettings.Current.ChaosPinOnTop;
        ShowInTaskbar = ShowActivated = CanResize = Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 640; Height = 400; Opacity = 0;
        Content = _host;
        _frames = new BeatLoop(this, _ => Advance());
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { Dismiss(); e.Handled = true; }
        };
        Closed += (_, _) => _frames.Stop();
    }

    internal static void Show(ChaosUnlockCardInfo info, Action? onDismissed = null, bool autoDismiss = true)
    {
        Queue.Enqueue((info, onDismissed, autoDismiss));
        if (Active?._card != null) return;
        try
        {
            Active ??= new ChaosUnlockCardOverlay();
            Active.Next();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Chaos unlock card display: {E}", ex.Message);
            CloseActive();
            Invoke(onDismissed); // failed presentation must not strand a held field
        }
    }

    internal static void CloseActive()
    {
        Queue.Clear();
        var w = Active;
        Active = null;
        if (w == null) return;
        w._frames.Stop();
        w._card = null;
        w._done = null;
        w._host.Children.Clear();
        w.Close();
    }

    private void Next()
    {
        if (Queue.Count == 0) return;
        var (info, done, auto) = Queue.Dequeue();
        _done = done; _auto = auto; _dismissing = false;
        _card = ChaosUnlockCardLayer.Build(info, 420);
        _card.Root.Opacity = 1;
        _card.Slide.Y = 0;
        _scale = new ScaleTransform(0.9, 0.9);
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = _scale,
        };
        panel.Children.Add(_card.Root);
        panel.Children.Add(new TextBlock
        {
            Text = "click to continue", FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        });
        var area = new Grid();
        area.Children.Add(panel);
        // Scrim spans the virtual desktop; card is centered on the primary work area.
        var screens = Screens.All;
        var primary = Screens.Primary ?? screens.FirstOrDefault();
        if (primary != null)
        {
            int left = screens.Min(s => s.Bounds.X), top = screens.Min(s => s.Bounds.Y);
            double scaling = primary.Scaling > 0 ? primary.Scaling : 1;
            Position = new PixelPoint(left, top);
            Width = (screens.Max(s => s.Bounds.Right) - left) / scaling;
            Height = (screens.Max(s => s.Bounds.Bottom) - top) / scaling;
            area.Width = primary.WorkingArea.Width / scaling;
            area.Height = primary.WorkingArea.Height / scaling;
            area.HorizontalAlignment = HorizontalAlignment.Left;
            area.VerticalAlignment = VerticalAlignment.Top;
            area.Margin = new Thickness((primary.WorkingArea.X - left) / scaling,
                (primary.WorkingArea.Y - top) / scaling, 0, 0);
        }
        _host.Children.Clear(); _host.Children.Add(area);
        _start = Now();
        base.Show();
        ConditioningControlPanel.Avalonia.Platform.X11Overlay.SetPassive(this);
        var (cue, vol) = info.Cue;
        ChaosOverlayWindow.ChaosSfx.Play(cue, vol);
        Advance(); _frames.Start();
    }

    internal void Dismiss()
    {
        if (_card == null || _dismissing) return;
        _dismissing = true;
        if (Queue.Count > 0)
        {
            var done = _done; _done = null;
            Invoke(done); Next(); // no fade-out between cards; IsShowing stays true in the callback
            return;
        }
        _fadeStart = Now(); _fadeOpacity = Opacity;
    }

    internal void Advance()
    {
        if (_card == null) return;
        double t = Math.Max(0, Now() - _start);
        if (_dismissing)
        {
            double u = Math.Clamp((Now() - _fadeStart) / OutMs, 0, 1);
            Opacity = _fadeOpacity * (1 - u);
            if (u < 1) return;
            _card = null; _host.Children.Clear(); _frames.Stop(); Hide();
            var done = _done; _done = null;
            Invoke(done);
            // A new card can arrive during the fade, or from the callback.
            if (_card == null && Queue.Count > 0) Next();
            return;
        }
        ChaosUnlockCardLayer.Apply(_card, Math.Min(t, 800)); // shared one-shot icon and glow, no hub fade
        _card.Root.Opacity = 1; _card.Slide.Y = 0;
        Opacity = Math.Clamp(t / InMs, 0, 1);
        _scale!.ScaleX = _scale.ScaleY = 0.9 + 0.1 * ChaosUnlockCardLayer.BackOut(Math.Min(1, t / PopMs), 0.5);
        if (_auto && t >= AutoMs) Dismiss();
    }

    private static void Invoke(Action? done)
    {
        try { done?.Invoke(); }
        catch (Exception ex) { Serilog.Log.Debug("Chaos unlock card callback: {E}", ex.Message); }
    }
}
