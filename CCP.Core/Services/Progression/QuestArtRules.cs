namespace ConditioningControlPanel.Services;

/// <summary>The built-in quest art swaps, after a mod override has had first refusal.
/// Heads resolve their own resources and decode the selected picture.</summary>
public static class QuestArtRules
{
    public static string? ModeReplacement(string resourceName, bool isSissyMode, bool isCcpDefault)
    {
        if ((isSissyMode || isCcpDefault) && resourceName.Contains("logo.png", System.StringComparison.Ordinal))
            return "logo2.png";
        if (isSissyMode && resourceName.Contains("bambi takeover.png", System.StringComparison.Ordinal))
            return "features/mandatory_videos.png";
        return null;
    }
}
