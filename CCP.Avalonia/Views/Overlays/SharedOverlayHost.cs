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
        X11Overlay.IsCompositing && X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);

    internal static Func<Window, bool> Raise = X11Overlay.Raise; // WPF SubliminalService.cs:829

    // WPF compositor: one host per screen, so consumers' ZIndex orders them (SpiralLayer above SubliminalLayer).
    internal sealed class Stage { internal Window Window = null!; internal Grid Canvas = null!; internal int Leases; }
    private static readonly Dictionary<PixelRect, Stage> Stages = new();

    internal sealed class Lease : IDisposable
    {
        private Stage? _stage;
        internal Grid Canvas { get; }
        internal Lease(Stage stage) => (_stage, Canvas) = (stage, stage.Canvas);
        internal void Raise() { if (_stage != null) SharedOverlayHost.Raise(_stage.Window); }
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
            Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
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
        return new Lease(stage);
    }
}
internal sealed class SharedOverlayStageWindow : Window { }
