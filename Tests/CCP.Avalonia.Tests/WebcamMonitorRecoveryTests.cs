using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamMonitorRecoveryTests
{
    private static Screen Display(string name, int x, bool primary = false, double scaling = 1)
    {
        // Avalonia hides PlatformScreen's native constructor in its reference assembly.
        // The synthetic handle is never passed to an OS; only screen metadata is exercised.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "synthetic-display") })!;
        void Set(string property, object value) => typeof(Screen).GetProperty(property)!.SetValue(screen, value);
        Set(nameof(Screen.DisplayName), name);
        Set(nameof(Screen.Bounds), new PixelRect(x, 0, 1920, 1080));
        Set(nameof(Screen.WorkingArea), screen.Bounds);
        Set(nameof(Screen.IsPrimary), primary);
        Set(nameof(Screen.Scaling), scaling);
        return screen;
    }

    [Fact]
    public void Returning_display_is_resolved_by_identity_without_changing_calibration()
    {
        var tracker = new WebcamTracker();
        var saved = new MonitorBoundsRecord { DeviceName = "DISPLAY2", X = 1920, Width = 1280, Height = 720, DpiScale = 1.5 };
        tracker.Calibration = new WebcamCalibrationData { MonitorBounds = saved };
        var primary = Display("DISPLAY1", 0, primary: true);
        Assert.Same(primary, tracker.GetGazeScreen(new[] { primary }));
        Assert.True(tracker.GazeMonitorFallback);
        int revision = tracker.GazeMonitorRevision;
        tracker.GetGazeScreen(new[] { primary });
        Assert.Equal(revision, tracker.GazeMonitorRevision);

        var returned = Display("display2", -1920, scaling: 1.25);
        Assert.Same(returned, tracker.GetGazeScreen(new Screen[] { returned, primary }));
        Assert.False(tracker.GazeMonitorFallback);
        Assert.True(tracker.GazeMonitorRevision > revision);
        Assert.Same(saved, tracker.Calibration.MonitorBounds);
        Assert.Equal("DISPLAY2", saved.DeviceName);
        Assert.Equal((1920, 1280, 720, 1.5), (saved.X, saved.Width, saved.Height, saved.DpiScale));

        revision = tracker.GazeMonitorRevision;
        tracker.GetGazeScreen(new Screen[] { Display("DISPLAY2", -2560), primary });
        Assert.True(tracker.GazeMonitorRevision > revision); // same name, different live geometry
        Assert.Null(tracker.GetGazeScreen(Array.Empty<Screen>()));
        Assert.True(tracker.GazeMonitorFallback);
        Assert.Same(returned, tracker.GetGazeScreen(new[] { returned }));
    }

    [Fact]
    public void Calibration_windows_follow_the_returned_display_instead_of_saved_origin() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var tracker = WebcamTracker.Instance;
        var old = tracker.Calibration;
        var window = new Window();
        try
        {
            tracker.Calibration = new WebcamCalibrationData
            {
                MonitorBounds = new MonitorBoundsRecord { DeviceName = "DISPLAY2", X = 1920, Y = 0 },
            };
            var primary = Display("DISPLAY1", 1920, primary: true);
            var returned = Display("DISPLAY2", -1920);
            WebcamCalibrationWindow.PlaceOnCalibratedScreen(window, new[] { primary, returned });
            Assert.Equal(new PixelPoint(-1920, 0), window.Position);
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            WebcamCalibrationWindow.PlaceOnCalibratedScreen(window, new[] { primary });
            Assert.Equal(primary.Bounds.Position, window.Position);
            WebcamCalibrationWindow.PlaceOnCalibratedScreen(window, new[] { returned, primary });
            Assert.Equal(returned.Bounds.Position, window.Position);
            Assert.Equal("DISPLAY2", tracker.Calibration.MonitorBounds!.DeviceName);
        }
        finally { window.Close(); tracker.Calibration = old; }
    });
    [Fact]
    public void Tracker_visualization_recovers_to_the_returned_display() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var tracker = WebcamTracker.Instance;
        var old = tracker.Calibration;
        WebcamGazeTrackerWindow? window = null;
        try
        {
            tracker.Calibration = new WebcamCalibrationData
            {
                MonitorBounds = new MonitorBoundsRecord { DeviceName = "DISPLAY2", X = 1920 },
            };
            window = new WebcamGazeTrackerWindow { WindowState = WindowState.Maximized };
            var primary = Display("DISPLAY1", 0, primary: true);
            window.RefreshMonitor(new[] { primary });
            Assert.Equal(primary.Bounds.Position, window.Position);
            var returned = Display("DISPLAY2", -1920);
            window.RefreshMonitor(new[] { returned, primary });
            Assert.Equal(returned.Bounds.Position, window.Position);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(1920, tracker.Calibration.MonitorBounds!.X);
        }
        finally { window?.Close(); tracker.Calibration = old; }
    });

}
