using System;

namespace ConditioningControlPanel.Services.UI;

/// <summary>The Home mosaic's off face stands half a hover lift proud of its socket.</summary>
public static class DashboardDepthLaw
{
    public static double TravelFor(bool enabled, bool pressed, bool active, bool hovered) =>
        enabled && !pressed && !active && !hovered
            ? -DepthLaw.HoverLiftPx / 2
            : DepthLaw.TravelFor(enabled, pressed, active, hovered);

    public static string BevelKeyFor(bool pressed, bool active) =>
        pressed || active ? "DepthPressedBevel" : "DepthRaisedBevel";

    public static double TiltFor(double nx, double ny) =>
        Math.Clamp(nx, -1, 1) * -Math.Clamp(ny, -1, 1) * DepthLaw.TiltDegrees;

    public static bool ToggleFor(bool dashboard, bool invert, bool right) => right != (dashboard && invert);
}
