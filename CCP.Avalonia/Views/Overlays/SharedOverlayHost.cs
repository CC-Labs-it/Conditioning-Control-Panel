using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF ChaosBubbleHostOverlay's passive click-through stage for one screen (its own DPI).
/// Each consumer keeps one lease per screen; the stage closes when its last lease is disposed. UI thread only.</summary>
// ponytail: per-screen legacy stages; existing chaos/tint consumers still need a compositor migration.
internal static class SharedOverlayHost
{
    // No compositor -> no per-pixel transparency: the stage would paint an opaque full-screen block for the
    // whole run. Refuse like PinkFilterOverlay/CornerGifOverlay; the caller falls back to classic cards.
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) =>
        X11Overlay.IsCompositing && X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds, topmost: w.Topmost);

    // WPF ChaosWindowZ.cs:41-52 and ChaosBubbleHostOverlay.cs:183: a Free Desktop (pin off) chaos run
    // keeps the stage out of the topmost band, born and on every raise.
    private static bool Pinned => !ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.IsActive
        || ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop;

    internal static Func<Window, bool> Raise = X11Overlay.Raise; // WPF SubliminalService.cs:829
    internal static Func<Window, IReadOnlyList<Window>, bool> StackBelow = X11Overlay.StackBelow;
    internal static Func<IReadOnlyList<Window>> LiveVideos = () => MandatoryVideoOverlay.Instance.Windows;

    // WPF draws tint, spiral and cards on one host, so a raise never lifts the spiral over an "over" tint
    // (OverlayService.RefreshPinkFilterOrder), and ReassertZOrder keeps that host below a playing video
    // (OverlayService.cs:3071,3389, #497). Here they are separate windows, so restore both orders by hand.
    // With a video up, stack the stage under it, then an "over" tint directly under it too: the order is
    // video > tint > stage, so the tint still covers the stage once the video closes.
    private static void Seat(Window stage, bool raise)
    {
        var videos = LiveVideos();
        if (videos.Count > 0)
        {
            StackBelow(stage, videos);
            if (!CoreSettings.Current.PinkFilterUnderEffects)
                foreach (var tint in PinkFilterOverlay.Shown) StackBelow(tint, videos);
            return;
        }
        if (raise) Raise(stage);
        if (!CoreSettings.Current.PinkFilterUnderEffects) PinkFilterOverlay.SyncOrder();
    }

    // WPF compositor: one host per screen, so consumers' ZIndex orders them (SpiralLayer above SubliminalLayer,
    // passive flashes under the cards): subliminal cards, flashes and spirals share it.
    internal sealed class Stage { internal Window Window = null!; internal Grid Canvas = null!; internal int Leases; }
    private static readonly Dictionary<PixelRect, Stage> Stages = new();

    internal sealed class Lease : IDisposable
    {
        private Stage? _stage;
        internal Grid Canvas { get; }
        internal Lease(Stage stage) => (_stage, Canvas) = (stage, stage.Canvas);
        internal void Raise()
        {
            if (_stage == null) return;
            if (!Pinned) { _stage.Window.Topmost = false; return; }
            Seat(_stage.Window, raise: true);
        }
        public void Dispose()
        {
            var s = _stage; _stage = null;
            if (s != null && --s.Leases == 0) s.Window.Close(); // the last consumer closes the stage
        }
    }

    internal static Lease? Acquire(PixelRect bounds)
    {
        if (Stages.TryGetValue(bounds, out var live)) { live.Leases++; return new Lease(live); }
        var canvas = new Grid { IsHitTestVisible = false };
        var window = new SharedOverlayStageWindow
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent, Topmost = Pinned, ShowInTaskbar = false,
            ShowActivated = false, CanResize = false, Focusable = false,
            IsHitTestVisible = false, Content = canvas,
        };
        try
        {
            if (!Configure(window, bounds)) { window.Close(); return null; }
            window.Show();
        }
        catch { window.Close(); return null; } // caller falls back to classic cards
        var stage = new Stage { Window = window, Canvas = canvas, Leases = 1 };
        Stages[bounds] = stage;
        // A panic sweep may close it under live leases: forget it so the next Acquire builds a new one.
        window.Closed += (_, _) => { if (Stages.TryGetValue(bounds, out var s) && s == stage) Stages.Remove(bounds); };
        Seat(window, raise: false);   // a fresh stage maps on top: also when a spiral starts mid-video
        return new Lease(stage);
    }
}
internal sealed class SharedOverlayStageWindow : Window { }
