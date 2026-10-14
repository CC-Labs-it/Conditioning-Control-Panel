using System.Globalization;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>Shared native rail gates and compact stage copy, from WPF SpiralRailHost.</summary>
public static class SpiralRailState
{
    public static bool Visible(bool enabled, bool hasBlock, bool spiralWithheld)
        => enabled && hasBlock && !spiralWithheld;

    public static string StageNumeral(int n)
        => n <= 0 ? "·" : n <= 8 ? DescentStageCopy.RomanNumeral(n) : n.ToString(CultureInfo.InvariantCulture);
}
