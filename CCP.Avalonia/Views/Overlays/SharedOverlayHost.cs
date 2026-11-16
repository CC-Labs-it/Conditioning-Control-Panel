using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF ChaosBubbleHostOverlay's passive click-through stage for one screen (its own DPI).
/// Leases on one screen share its stage; disposing the last closes it. UI thread only.</summary>
// ponytail: per-screen legacy stages; existing chaos/tint consumers still need a compositor migration.
internal static class SharedOverlayHost
{
    // No compositor -> no per-pixel transparency: the stage would paint an opaque full-screen block for the
    // whole run. Refuse like PinkFilterOverlay/CornerGifOverlay; the caller falls back to classic cards.
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) =>
        X11Overlay.IsCompositing && X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);

    internal static Func<Window, bool> Raise = X11Overlay.Raise; // WPF SubliminalService.cs:829

    // One stage per screen, shared by its owners (subliminal cards, passive flashes); the last lease closes it.
    private static readonly Dictionary<PixelRect, (Window Window, Grid Canvas, int Refs)> Stages = new();

    internal sealed class Lease : IDisposable
    {
        private readonly PixelRect _bounds;
        private Window? _window;
        internal Grid Canvas { get; }
        internal Lease(PixelRect bounds, Window window, Grid canvas) => (_bounds, _window, Canvas) = (bounds, window, canvas);
        internal void Raise() { if (_window != null) SharedOverlayHost.Raise(_window); }
        public void Dispose()
        {
            var w = _window; _window = null;
            if (w == null) return;
            if (Stages.TryGetValue(_bounds, out var stage) && stage.Window == w && stage.Refs > 1)
                Stages[_bounds] = (w, stage.Canvas, stage.Refs - 1);
            else w.Close();
        }
    }

    internal static Lease? Acquire(PixelRect bounds)
    {
        if (Stages.TryGetValue(bounds, out var live))
        {
            Stages[bounds] = (live.Window, live.Canvas, live.Refs + 1);
            return new Lease(bounds, live.Window, live.Canvas);
        }
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
        Stages[bounds] = (window, canvas, 1);
        window.Closed += (_, _) => { if (Stages.TryGetValue(bounds, out var s) && s.Window == window) Stages.Remove(bounds); };
        return new Lease(bounds, window, canvas);
    }
}
internal sealed class SharedOverlayStageWindow : Window { }
