using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF ChaosBubbleHostOverlay's passive click-through stage for one screen (its own DPI).
/// The caller keeps one lease per screen; disposing it closes the stage. UI thread only.</summary>
// ponytail: per-screen legacy stages; existing chaos/tint consumers still need a compositor migration.
internal static class SharedOverlayHost
{
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) =>
        X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);

    internal static Func<Window, bool> Raise = X11Overlay.Raise; // WPF SubliminalService.cs:829

    internal sealed class Lease : IDisposable
    {
        private Window? _window;
        internal Grid Canvas { get; }
        internal Lease(Window window, Grid canvas) => (_window, Canvas) = (window, canvas);
        internal void Raise() { if (_window != null) SharedOverlayHost.Raise(_window); }
        public void Dispose() { var w = _window; _window = null; w?.Close(); }
    }

    internal static Lease? Acquire(PixelRect bounds)
    {
        var canvas = new Grid { IsHitTestVisible = false };
        var window = new Window
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
        return new Lease(window, canvas);
    }
}
