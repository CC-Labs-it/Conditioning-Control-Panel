using System;
using System.Globalization;

namespace ConditioningControlPanel;

/// <summary>WPF's tutorial text matcher, shared by both coach-mark heads.</summary>
public static class TutorialAdvanceRules
{
    public static bool TextMatches(string? actual, string? expected)
    {
        actual = (actual ?? "").Trim();
        expected = (expected ?? "").Trim();
        if (expected.Length == 0) return false;
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) &&
            double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var ev))
            return Math.Abs(av - ev) < 0.5;
        return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
