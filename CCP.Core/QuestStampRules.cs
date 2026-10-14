using System;

namespace ConditioningControlPanel;

/// <summary>WPF quest-stamp display rules shared by both heads.</summary>
public static class QuestStampRules
{
    public static double Fraction(int current, int target) => target <= 0 ? 0 : Math.Clamp((double)current / target, 0, 1);
    public static string Text(string? localized, string fallback) =>
        string.IsNullOrWhiteSpace(localized) || localized.StartsWith("quest_", StringComparison.Ordinal) ? fallback : localized;
    public static (double Fraction, int Current, int Remaining) Progress(int current, int target, bool completed) =>
        (completed ? 1 : Fraction(current, target), completed ? Math.Max(current, target) : current,
            completed ? 0 : Math.Max(0, target - current));

    // Heads apply these built-in resource swaps only after checking their own mod/file paths.
    public static string? LegacyImageOverride(string resource, bool sissy, bool ccpDefault)
    {
        if ((sissy || ccpDefault) && resource.Contains("logo.png", StringComparison.Ordinal)) return "logo2.png";
        if (sissy && resource.Contains("bambi takeover.png", StringComparison.Ordinal)) return "features/mandatory_videos.png";
        return null;
    }
}
