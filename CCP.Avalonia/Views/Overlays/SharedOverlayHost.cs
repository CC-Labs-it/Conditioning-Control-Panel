using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF ChaosBubbleHostOverlay's ownership contract: passive click-through stage,
/// retained until the last owner releases it. UI thread only. Each screen uses its own DPI.</summary>
// ponytail: per-screen legacy stages; existing chaos/tint consumers still need a compositor migration.
internal static class SharedOverlayHost
{
    private sealed record Stage(Window Window, Grid Canvas)
    {
        internal int Owners;
    }
    private static readonly Dictionary<PixelRect, Stage> Stages = new();
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) =>
        X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);

    internal sealed class Lease : IDisposable
    {
        private Action? _release;
        internal Grid Canvas { get; }
        internal Lease(Grid canvas, Action release) => (Canvas, _release) = (canvas, release);
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }

    internal static Lease? Acquire(PixelRect bounds)
    {
        if (!Stages.TryGetValue(bounds, out var stage))
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
            stage = new Stage(window, canvas);
            Stages.Add(bounds, stage);
        }
        stage.Owners++;
        var held = stage;
        return new Lease(stage.Canvas, () =>
        {
            if (--held.Owners != 0) return;
            Stages.Remove(bounds);
            held.Window.Close();
        });
    }
}
