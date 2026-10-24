using System;
using System.Collections.Generic;

namespace ConditioningControlPanel;

/// <summary>
/// The envelope of the Chaos "braindrain" wash (WPF ConditioningControlPanel/Chaos/ChaosFlashOverlay.cs):
/// fade in from 0 to the peak over 500 ms (:287), hold until the life timer fires at
/// max(600, duration) after the wash began (:288), then fade to 0 over 700 ms (:180). Linear, as
/// WPF's DoubleAnimation is. Every head draws the same curve from here.
/// </summary>
public static class ChaosFlashWash
{
    /// <summary>WPF :32 - a chaos wash with no duration given is ~10 s on screen.</summary>
    public const int DefaultDurationMs = 10000;
    /// <summary>WPF :33 - the faint 10% wash.</summary>
    public const double DefaultOpacity = 0.10;
    public const int FadeInMs = 500;
    public const int FadeOutMs = 700;
    public const int MinLifeMs = 600;
    /// <summary>WPF :222/:231 - animations decode capped at 1280 px (stills go up to 2560, :254).</summary>
    public const int MaxDecodeDim = 1280;
    /// <summary>WPF :222/:231 - at most 40 animation frames resident (#476 OOM).</summary>
    public const int MaxFrames = 40;

    /// <summary>One wash: its peak alpha and when the fade-out starts (ms after the wash began).</summary>
    public readonly record struct Plan(double Peak, int FadeOutAtMs)
    {
        public int EndMs => FadeOutAtMs + FadeOutMs;
    }

    /// <summary>WPF DisplayCore: peak clamped to 0.02..1 (:286), life at least 600 ms (:288).</summary>
    public static Plan For(int durationMs, double opacity)
        => new(double.IsNaN(opacity) ? 0.02 : Math.Clamp(opacity, 0.02, 1.0), Math.Max(MinLifeMs, durationMs));

    /// <summary>The wash's alpha <paramref name="ms"/> after it began.</summary>
    public static double AlphaAt(Plan p, double ms)
    {
        if (ms <= 0) return 0;
        if (ms < FadeInMs) return p.Peak * ms / FadeInMs;
        if (ms < p.FadeOutAtMs) return p.Peak;
        var t = ms - p.FadeOutAtMs;
        return t >= FadeOutMs ? 0 : p.Peak * (1 - t / FadeOutMs);
    }

    /// <summary>The curve sampled at <paramref name="steps"/> points per ramp, for a head that writes
    /// the alpha on one-shot timers (no per-frame loop). The last entry is 0 at <see cref="Plan.EndMs"/>.</summary>
    public static IReadOnlyList<(int AtMs, double Alpha)> Keyframes(Plan p, int steps = 16)
    {
        steps = Math.Max(1, steps);
        var list = new List<(int, double)>(steps * 2);
        for (var i = 1; i <= steps; i++)
        {
            var t = FadeInMs * i / steps;
            list.Add((t, AlphaAt(p, t)));
        }
        for (var i = 1; i <= steps; i++)
        {
            var t = p.FadeOutAtMs + FadeOutMs * i / steps;
            list.Add((t, AlphaAt(p, t)));
        }
        return list;
    }
}
