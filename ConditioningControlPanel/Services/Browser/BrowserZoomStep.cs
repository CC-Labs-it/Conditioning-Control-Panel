using System;

namespace ConditioningControlPanel.Services.Browser;

// ccp-bugs #1694. The + / - page-zoom buttons on the dashboard browser's toolbar. Same steps and
// clamp as the Deeper player's BrowserZoomCluster (EnhancementPlayerWindow.AdjustVideoZoom):
// 10% a press, never below 25% (unreadable) or above 500% (eats RAM). Rounded to whole percent so
// repeated presses from the 50% start land on 60%, 70% ... and not on 0.7000000000000001.
internal static class BrowserZoomStep
{
    public const double Step = 0.10;
    public const double Min = 0.25;
    public const double Max = 5.0;

    /// <summary>The zoom factor one press of + (<paramref name="direction"/> &gt; 0) or
    /// - (&lt; 0) leads to from <paramref name="current"/>.</summary>
    public static double Next(double current, int direction)
    {
        if (double.IsNaN(current) || double.IsInfinity(current) || current <= 0) current = 1.0;
        var next = current + Math.Sign(direction) * Step;
        return Math.Round(Math.Clamp(next, Min, Max), 2);
    }
}
