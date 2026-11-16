using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>
/// The safe window and dim layer for Lights Down, driven by the mandatory video host.
/// WPF: Services/Super/LightsDownOverlay.cs:14 and LightsDownService.cs:89.
/// All calls are on the UI thread.
/// </summary>
internal static partial class LightsDownOverlay
{
    private static readonly List<Window> Windows = new();
    internal static int Count => Windows.Count;

    // The optional probes let headless tests exercise refusal without touching a desktop.
    internal static bool TryOpen(Window video, PixelRect bounds, double scale,
        Func<Window, bool>? eligible = null, Func<Window, bool>? clickThrough = null,
        Func<Window, bool>? transparent = null, Func<Window, Window, bool>? below = null)
    {
        eligible ??= w => X11Overlay.BackendOf(w.TryGetPlatformHandle()) == OverlayBackend.X11
            && X11Overlay.IsAvailable && X11Overlay.IsCompositing;
        clickThrough ??= w => X11Overlay.SetClickThrough(w, true);
        transparent ??= w => w.ActualTransparencyLevel != WindowTransparencyLevel.None;
        below ??= (w, v) => X11Overlay.RestackAbove(v, w);
        if (!eligible(video)) return false;

        var w = new Window
        {
            SystemDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            ShowActivated = false, ShowInTaskbar = false, CanResize = false, Topmost = true,
            Focusable = false, IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Opacity = 0,
            Content = new Border { Background = new SolidColorBrush(Color.FromRgb(3, 1, 9)) }
        };
        try
        {
            // Set the empty XFixes input region before mapping. Refuse if this XID cannot
            // be made click-through; no mapped full-screen window may receive input.
            if (!clickThrough(w)) { w.Close(); return false; }
            scale = scale > 0 ? scale : 1;
            w.Position = new PixelPoint(bounds.X, bounds.Y);
            w.Width = bounds.Width / scale;
            w.Height = bounds.Height / scale;
            w.Show();
            if (!transparent(w) || !below(w, video)) { w.Close(); return false; }
            Windows.Add(w);
            w.Closed += (_, _) => Windows.Remove(w);
            return true;
        }
        catch
        {
            w.Close();
            return false;
        }
    }

    /// <summary>Copy the shared frame's dim to every screen, without advancing its state.</summary>
    internal static void Apply(LightsDownFrame frame)
    {
        foreach (var w in Windows) w.Opacity = Math.Clamp(frame.DimAlpha, 0, 1);
    }

    internal static void CloseAll()
    {
        StopVideo();
        foreach (var w in Windows.ToArray())
            try { w.Close(); } catch (Exception ex) { Serilog.Log.Debug(ex, "Lights Down close failed"); }
        Windows.Clear();
    }
}
