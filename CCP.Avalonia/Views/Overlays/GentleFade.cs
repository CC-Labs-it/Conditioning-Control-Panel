using System;
using System.Diagnostics;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal static class GentleFade
{
    // ponytail: finite frame sampling reuses the head's render clock; no animation survives a close.
    internal static void Run(Window window, double from, double to, double seconds, Func<bool> current, Action? done = null, Action<double>? setOpacity = null)
    {
        var start = Stopwatch.GetTimestamp();
        setOpacity ??= value => window.Opacity = value;
        setOpacity(Math.Clamp(from, 0, 1));
        void Frame(TimeSpan _)
        {
            if (!current() || !window.IsVisible) return;
            double p = Math.Clamp(Stopwatch.GetElapsedTime(start).TotalSeconds / seconds, 0, 1);
            setOpacity(Math.Clamp(from + (to - from) * p, 0, 1));
            if (p < 1) window.RequestAnimationFrame(Frame);
            else done?.Invoke();
        }
        window.RequestAnimationFrame(Frame);
    }
}
