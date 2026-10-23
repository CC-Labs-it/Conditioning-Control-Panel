using System;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>WPF FypHostService.OnEyeGaze: calibrated monitor DIPs → desktop pixels → web client 0..1.</summary>
internal static class FypGazeRules
{
    public static (double X, double Y, bool Inside)? Normalize(double x, double y,
        double calOriginX, double calOriginY, double calScale, double webOriginX, double webOriginY,
        double webScale, double width, double height)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height)) return null;
        if (!(calScale > 0.25 && calScale < 8)) calScale = 1;
        if (webScale <= 0 || !double.IsFinite(webScale)) webScale = 1;
        double nx = ((x * calScale + calOriginX - webOriginX) / webScale) / width;
        double ny = ((y * calScale + calOriginY - webOriginY) / webScale) / height;
        if (!double.IsFinite(nx) || !double.IsFinite(ny)) return null;
        bool inside = nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1;
        return (Math.Clamp(nx, 0, 1), Math.Clamp(ny, 0, 1), inside);
    }
}
