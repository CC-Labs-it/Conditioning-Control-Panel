using System;

namespace ConditioningControlPanel.Services.Program;

/// <summary>The live Programs slot's bounded elapsed/total clock (WPF ProgramsTab).</summary>
public readonly record struct ProgramSessionProgress(TimeSpan Elapsed, TimeSpan Total, double Percent)
{
    public static ProgramSessionProgress From(TimeSpan elapsed, int durationMinutes)
    {
        var total = TimeSpan.FromMinutes(Math.Max(1, durationMinutes));
        elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed > total ? total : elapsed;
        return new(elapsed, total, elapsed.TotalSeconds / total.TotalSeconds * 100);
    }

    /// <summary>WPF FormatProgramClock: m:ss, h:mm:ss after an hour, never negative.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
    }
}
