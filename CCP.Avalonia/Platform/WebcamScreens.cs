using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using ConditioningControlPanel.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class WebcamScreens
{
    // Resolve against the current topology every time: unplug/replug never edits saved calibration.
    internal static Screen? Resolve(Screens screens, string? name)
    {
        var all = screens.All;
        var choice = WebcamMonitorSelection.Select(name, all.Select(s => s.DisplayName ?? "").ToArray(),
            all.ToList().FindIndex(s => s.IsPrimary));
        return choice.Index >= 0 ? all[choice.Index] : null;
    }

    internal static void Place(Window window)
    {
        if (window.Screens is not { } screens) return;
        string? name = CoreSettings.Current.WebcamCalibrationScreen;
        if (string.Equals(name, "Primary", StringComparison.OrdinalIgnoreCase)) name = null;
        if (Resolve(screens, name) is not { } screen) return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = screen.Bounds.Position;
    }
}
