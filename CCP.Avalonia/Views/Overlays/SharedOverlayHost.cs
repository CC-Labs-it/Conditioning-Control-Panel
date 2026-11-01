using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>One passive stage per screen, shared by overlay families. UI thread only.</summary>
internal static class SharedOverlayHost
{
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) =>
        X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);
    internal static Func<Window, bool> IsTransparent = w => w.ActualTransparencyLevel != WindowTransparencyLevel.None;
    internal static Func<Window, bool> Raise = X11Overlay.Raise; // WPF SubliminalService.cs:829

    private static readonly Dictionary<PixelRect, (Window Window, Grid Canvas, int Owners)> Stages = new();

    internal sealed class Lease : IDisposable
    {
        private readonly PixelRect _bounds;
        private Window? _window;
        internal Grid Canvas { get; }
        internal bool IsOpen => _window?.IsVisible == true;
        internal Lease(PixelRect bounds, Window window, Grid canvas) => (_bounds, _window, Canvas) = (bounds, window, canvas);
        internal void Raise() { if (IsOpen) SharedOverlayHost.Raise(_window!); }
        public void Dispose()
        {
            var window = _window;
            _window = null;
            if (window == null || !Stages.TryGetValue(_bounds, out var stage) || stage.Window != window) return;
            if (stage.Owners > 1) Stages[_bounds] = (stage.Window, stage.Canvas, stage.Owners - 1);
            else { Stages.Remove(_bounds); window.Close(); }
        }
    }

    internal static Lease? Acquire(PixelRect bounds)
    {
        if (Stages.TryGetValue(bounds, out var stage))
        {
            Stages[bounds] = (stage.Window, stage.Canvas, stage.Owners + 1);
            return new Lease(bounds, stage.Window, stage.Canvas);
        }
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
            // A full-screen opaque stage would hide the desktop. Let each caller fall back.
            if (!IsTransparent(window)) { window.Close(); return null; }
        }
        catch { window.Close(); return null; }
        Stages.Add(bounds, (window, canvas, 1));
        window.Closed += (_, _) =>
        {
            if (Stages.TryGetValue(bounds, out var current) && current.Window == window) Stages.Remove(bounds);
        };
        return new Lease(bounds, window, canvas);
    }
}
