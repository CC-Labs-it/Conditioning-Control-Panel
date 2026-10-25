using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.GetWebcamCalibrationScreen/ApplyCalibrationScreenPlacement: the saved choice,
/// then primary, then the first display. DisplayName is Avalonia's device identity.</summary>
internal static class WebcamScreens
{
    internal static Func<IReadOnlyList<Screen>> Enumerate = () =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Screens.All
        ?? Array.Empty<Screen>();

    internal static string Name(Screen screen) => screen.DisplayName ?? $"{screen.Bounds.X},{screen.Bounds.Y}";

    internal static Screen? Resolve(IReadOnlyList<Screen> screens, string? saved) =>
        (!string.IsNullOrEmpty(saved) && !string.Equals(saved, "Primary", StringComparison.OrdinalIgnoreCase)
            ? screens.FirstOrDefault(s => string.Equals(Name(s), saved, StringComparison.OrdinalIgnoreCase)) : null)
        ?? screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault();

    internal static void Place(Window window)
    {
        if (Resolve(window.Screens.All, CoreSettings.Current.WebcamCalibrationScreen) is not { } screen) return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = screen.Bounds.Position;
    }
}
