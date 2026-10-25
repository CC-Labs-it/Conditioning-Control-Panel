using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperWebcamSetupTests
{
    [Fact]
    public Task DeeperConsentButtonsUseTheSharedDialog_AndRefreshBothCards() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        MainShellWindow? shell = null;
        try
        {
            s.WebcamConsentGiven = false;
            s.WebcamConsentVersion = "";
            tracker.Calibration = null;
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            var deeper = shell.FindControl<DeeperTabView>("DeeperTab")!;
            var blink = shell.FindControl<BlinkTrainerTabView>("BlinkTrainerTab")!;
            Assert.Equal(Loc.Get("blink_trainer_consent_required"), deeper.FindControl<TextBlock>("DeeperWebcamConsentStatus")!.Text);
            Assert.False(deeper.FindControl<Button>("BtnDeeperWebcamRevokeConsent")!.IsVisible);
            Assert.Equal(Color.FromRgb(0xFF, 0xD0, 0x80), ((ISolidColorBrush)deeper.FindControl<Border>("DeeperWebcamConsentCard")!.BorderBrush!).Color);
            // WPF UpdateBlinkTrainerTrackerButton (MainWindow.BlinkTrainer.cs:378): the label follows the tracker.
            Assert.Equal("Start tracker", deeper.FindControl<Button>("BtnDeeperWebcamStartStopTracker")!.Content);

            // The free Deeper card uses the same modal even while the premium Blink Trainer tab is hidden.
            deeper.FindControl<Button>("BtnDeeperWebcamManageConsent")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => shell.OwnedWindows.OfType<WebcamConsentDialog>().Any());
            var dialog = Assert.Single(shell.OwnedWindows.OfType<WebcamConsentDialog>());
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            dialog.Close();
            await Until(() => deeper.FindControl<Button>("BtnDeeperWebcamRevokeConsent")!.IsVisible);
            Assert.Equal(Loc.Get("blink_trainer_consent_granted"), deeper.FindControl<TextBlock>("DeeperWebcamConsentStatus")!.Text);
            Assert.True(blink.FindControl<Button>("BtnBlinkTrainerRevokeConsent")!.IsVisible);

            tracker.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DeviceName = "TEST-MONITOR" } };
            // Showing the setup card refreshes a calibration changed by another surface.
            deeper.IsVisible = false;
            deeper.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.GetF("blink_trainer_calibration_calibrated_format", "TEST-MONITOR"),
                deeper.FindControl<TextBlock>("DeeperWebcamCalibrationStatus")!.Text);

            deeper.FindControl<Button>("BtnDeeperWebcamRevokeConsent")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
            var confirmation = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.True(confirmation.FindControl<Button>("BtnCancel")!.IsDefault);
            confirmation.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !shell.OwnedWindows.OfType<MessageDialog>().Any());
            // Cancelling leaves the shared consent intact and never opens a camera.
            Assert.True(WebcamConsent.IsCurrent(s));
            Assert.False(tracker.IsRunning);
            deeper.FindControl<Border>("DeeperWebcamSetupCard")!.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content is TextBlock t && t.Text == Loc.Get("set2_configure_in_settings"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("devices", shell.FindControl<AppSettingsTabView>("AppSettingsTab")!.CheckedSectionKey);
        }
        finally
        {
            foreach (var window in shell?.OwnedWindows.ToArray() ?? Array.Empty<Window>()) window.Close();
            shell?.RequestExit();
            (s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
            tracker.Calibration = oldCal;
        }
    });

    [Theory]
    [InlineData("tracker")]
    [InlineData("quick")]
    [InlineData("calibrate")]
    public Task DeeperCameraActionsUseExistingWorkflows(string action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion);
        MainShellWindow? shell = null;
        try
        {
            s.WebcamConsentGiven = false;
            s.WebcamConsentVersion = "";
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            var deeper = shell.FindControl<DeeperTabView>("DeeperTab")!;
            var button = action == "tracker" ? deeper.FindControl<Button>("BtnDeeperWebcamStartStopTracker")!
                : deeper.FindControl<Border>("DeeperWebcamSetupCard")!.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.Content is TextBlock t && t.Text == Loc.Get(action == "quick"
                        ? "blink_trainer_calibration_quick" : "blink_trainer_calibration_btn"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => action == "calibrate"
                ? shell.OwnedWindows.OfType<WebcamCalibrationWindow>().Any()
                : shell.OwnedWindows.OfType<WebcamConsentDialog>().Any());
            Assert.False(WebcamTracker.Instance.IsRunning); // stale consent never opens a real camera
        }
        finally
        {
            foreach (var window in shell?.OwnedWindows.ToArray() ?? Array.Empty<Window>()) window.Close();
            shell?.RequestExit();
            (s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
        }
    });

    // No real-time wait (test-sleeps ratchet): ShowDialogSafe opens dialogs synchronously, so draining
    // the dispatcher queue is enough.
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(condition());
    }
}
