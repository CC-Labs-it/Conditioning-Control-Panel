using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// ponytail: the existing window hosts use finite fades; no new compositor or ambient clock.
internal static class GentleOverlayFade
{
    private sealed record Fade(double Alpha, bool Leaving);
    private static readonly Dictionary<Window, Fade> Fades = new();
    private static DispatcherTimer? _timer;
    private static long _last;
    internal static Action<Window, double> WriteOpacity = (w, a) =>
    {
        if (!Platform.X11Overlay.SetOpacity(w, a)) w.Opacity = a;
    };

    internal static void Show(Window window)
    {
        if (!GentleVisuals.On) return;
        WriteOpacity(window, 0);
        Track(window, new Fade(0, false));
    }

    internal static void Close(Window window)
    {
        if (!GentleVisuals.On) { window.Close(); return; }
        Track(window, new Fade(Fades.TryGetValue(window, out var f) ? f.Alpha : 1, true));
    }

    private static void Track(Window window, Fade fade)
    {
        if (!Fades.ContainsKey(window)) window.Closed += (_, _) => Remove(window);
        Fades[window] = fade;
        if (_timer != null) return;
        _last = Environment.TickCount64;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            var now = Environment.TickCount64;
            Advance(TimeSpan.FromMilliseconds(now - _last));
            _last = now;
        });
        _timer.Start();
    }

    internal static void Advance(TimeSpan delta)
    {
        foreach (var (window, fade) in Fades.ToArray())
        {
            var target = fade.Leaving ? 0 : 1;
            var alpha = GentleFade.Step(fade.Alpha, target, delta.TotalSeconds, GentleVisuals.FadeSeconds);
            WriteOpacity(window, Math.Clamp(alpha, 0, 1));
            if (alpha != target) Fades[window] = fade with { Alpha = alpha };
            else
            {
                Remove(window);
                if (fade.Leaving) window.Close();
            }
        }
    }

    private static void Remove(Window window)
    {
        Fades.Remove(window);
        if (Fades.Count != 0) return;
        _timer?.Stop();
        _timer = null;
    }

    // A stopped service drops its windows immediately; retain their fades here for panic.
    internal static void CloseAll(Func<Window, bool>? matches = null)
    {
        foreach (var window in Fades.Keys.Where(w => matches?.Invoke(w) ?? true).ToArray())
        { Remove(window); window.Close(); }
    }
}
