using System;
using Avalonia;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamMonitorRecoveryTests
{
    // Avalonia's reference API does not expose PlatformScreen's runtime constructor.
    // Build isolated screen objects without touching the platform's real display set.
    private static Screen TestScreen(string name, int x, int y, double scale, bool primary = false)
    {
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "test") })!;
        typeof(Screen).GetProperty(nameof(Screen.DisplayName))!.SetValue(screen, name);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, new PixelRect(x, y, 1920, 1080));
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, scale);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(0.25, 1)]
    [InlineData(8, 1)]
    [InlineData(double.NaN, 1)]
    public void ReturnedMonitorUsesLiveOriginAndSavedCalibrationScale(double savedScale, double expectedScale)
        => AvaloniaTestDispatcher.Run(() =>
        {
            var tracker = WebcamTracker.Instance;
            var previousCalibration = tracker.Calibration;
            var enumerate = WebcamScreens.Enumerate;
            var bounds = new MonitorBoundsRecord
            {
                DeviceName = "SECONDARY", X = 1920, Y = 0, DpiScale = savedScale,
            };
            var calibration = new WebcamCalibrationData { MonitorBounds = bounds };
            var primary = TestScreen("PRIMARY", 0, 0, 1, primary: true);
            var original = TestScreen("SECONDARY", 1920, 0, expectedScale);
            var returned = TestScreen("secondary", -2560, 300, 3);
            var gaze = new Point(100, 50);
            try
            {
                tracker.Calibration = calibration;
                WebcamScreens.Enumerate = () => new Screen[] { primary, original };
                Assert.Equal(new Point(100 + 1920 / expectedScale, 50), GazeFocus.GazeToDesktop(gaze));

                WebcamScreens.Enumerate = () => new Screen[] { primary };
                Assert.Equal(gaze, GazeFocus.GazeToDesktop(gaze));

                // The returned display has a new origin, scale and enumeration order.
                WebcamScreens.Enumerate = () => new Screen[] { returned, primary };
                Assert.Equal(new Point(100 - 2560 / expectedScale, 50 + 300 / expectedScale),
                    GazeFocus.GazeToDesktop(gaze));
                Assert.Same(calibration, tracker.Calibration);
                Assert.Equal("SECONDARY", bounds.DeviceName);
                Assert.Equal(1920, bounds.X);
                Assert.Equal(0, bounds.Y);
                Assert.Equal(savedScale, bounds.DpiScale);
            }
            finally
            {
                tracker.Calibration = previousCalibration;
                WebcamScreens.Enumerate = enumerate;
            }
        });
}
