using System;
using System.Linq;
using System.Reflection;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DevicesGazeCursorTests
{
    private static T? Event<T>(string name) where T : Delegate =>
        (T?)typeof(WebcamTracker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(WebcamTracker.Instance);

    [Fact]
    public void ToggleRoutesGazeAndCleansUpOnFaceLossStopPanicAndShellClose() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var make = GazeDebugCursor.MakeClickThrough;
        var available = CoreWebcam.IsAvailableProvider;
        var calibration = WebcamTracker.Instance.Calibration;
        CoreWebcam.IsAvailableProvider = () => true;
        GazeDebugCursor.MakeClickThrough = _ => true;
        var shell = new MainShellWindow();
        var section = new DevicesSettingsSection();
        shell.Content = section;
        shell.Show();
        var toggle = section.FindControl<CheckBox>("ChkWebcamDebugCursor")!;
        try
        {
            Assert.True(toggle.IsEnabled);
            toggle.IsChecked = true;
            var cursor = Assert.IsType<GazeDebugCursor>(GazeDebugCursor.Active);
            var screen = cursor.Screens.Primary!;
            WebcamTracker.Instance.Calibration = new WebcamCalibrationData
            {
                MonitorBounds = new MonitorBoundsRecord { X = screen.Bounds.X, Y = screen.Bounds.Y, DpiScale = screen.Scaling }
            };
            Event<Action<Point>>("OnGazeMove")?.Invoke(new Point(30, 40));
            Assert.True(cursor.HasGaze);
            Assert.True(cursor.ClockRunning);
            Assert.Equal(1, cursor.TrailCount);
            for (int i = 0; i < 6; i++) cursor.Advance(.1);
            Assert.Equal(0, cursor.TrailCount);
            Assert.False(cursor.ClockRunning);
            Assert.True(cursor.HasGaze); // stationary dot outlives its trail
            Event<Action>("OnFaceLost")?.Invoke();
            Assert.False(cursor.HasGaze);
            Event<Action<Point>>("OnGazeMove")?.Invoke(new Point(40, 50));
            Assert.True(cursor.HasGaze);
            Event<Action>("StateChanged")?.Invoke(); // tracker is stopped
            Assert.False(cursor.HasGaze);
            Assert.False(cursor.ClockRunning);
            Assert.Equal(0, cursor.TrailCount);
            Assert.True(cursor.Topmost);
            Assert.False(cursor.ShowActivated || cursor.IsHitTestVisible);
            PanicSurfaces.All.Single(p => p.Id == "gaze-debug-cursor").Stop(shell);
            Assert.Null(GazeDebugCursor.Active);
            Assert.False(toggle.IsChecked);
            // Only the overlay's delegates disappeared, not another caller's subscriptions.
            Assert.DoesNotContain(Event<Action<Point>>("OnGazeMove")?.GetInvocationList() ?? [], d => ReferenceEquals(d.Target, cursor));
            Assert.DoesNotContain(Event<Action>("OnFaceLost")?.GetInvocationList() ?? [], d => ReferenceEquals(d.Target, cursor));
            GazeDebugCursor.MakeClickThrough = _ => false;
            toggle.IsChecked = true;
            Assert.Null(GazeDebugCursor.Active);
            Assert.False(toggle.IsChecked);
            Assert.Contains(Loc.Get("set2_cursor_unavailable"), section.FindControl<TextBlock>("TxtWebcamDebugLog")!.Text);
            GazeDebugCursor.MakeClickThrough = _ => true;
            toggle.IsChecked = true;
            Assert.NotNull(GazeDebugCursor.Active);
            toggle.IsChecked = false;
            Assert.Null(GazeDebugCursor.Active);
            toggle.IsChecked = true;
            section.FindControl<Button>("BtnWebcamRevokeConsent")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var confirm = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.True(confirm.FindControl<Button>("BtnCancel")!.IsDefault);
            confirm.Close(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(GazeDebugCursor.Active);
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            shell.Close();
            Assert.Null(GazeDebugCursor.Active);
        }
        finally
        {
            GazeDebugCursor.CloseActive();
            shell.Close();
            GazeDebugCursor.MakeClickThrough = make;
            CoreWebcam.IsAvailableProvider = available;
            WebcamTracker.Instance.Calibration = calibration;
        }
    });
}
