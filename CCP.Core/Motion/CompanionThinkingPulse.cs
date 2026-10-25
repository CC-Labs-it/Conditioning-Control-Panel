using System;

namespace ConditioningControlPanel.Motion;

/// <summary>WPF CmpThinkingDotsStoryboard: .25 to 1, 450 ms each way, with 150 ms per-dot starts.</summary>
public static class CompanionThinkingPulse
{
    public const double HalfCycleMilliseconds = 450;
    public const double StaggerMilliseconds = 150;
    public const double MinimumOpacity = 0.25;

    public static double Opacity(double elapsedMilliseconds, int dot)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0) return 1;
        var delay = StaggerMilliseconds * Math.Clamp(dot, 0, 2);
        // Each WPF clone repeats its whole group: BeginTime is part of that group's duration.
        var phase = elapsedMilliseconds % (HalfCycleMilliseconds * 2 + delay);
        if (phase < delay) return 1; // the base opacity while that clone has not begun
        phase -= delay;
        var progress = phase <= HalfCycleMilliseconds
            ? phase / HalfCycleMilliseconds : 2 - phase / HalfCycleMilliseconds;
        return MinimumOpacity + (1 - MinimumOpacity) * progress;
    }
}
