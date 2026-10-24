using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperWebcamCardTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Click(Control host, string name)
    {
        host.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void CardReflectsCurrentConsentAndCalibration() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.Instance.Calibration);
        Window? host = null;
        try
        {
            s.WebcamConsentGiven = false;
            WebcamTracker.Instance.Calibration = null;
            var view = new DeeperTabView();
            host = new Window { Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var vm = (DeeperTabViewModel)view.DataContext!;
            Assert.Equal(Loc.Get("blink_trainer_consent_required"), vm.ConsentStatusText);
            Assert.Equal(Loc.Get("blink_trainer_consent_grant"), vm.ConsentButtonText);
            Assert.False(view.FindControl<Button>("BtnDeeperWebcamRevokeConsent")!.IsVisible);
            Assert.Equal(Color.Parse("#FFD080"), ((ISolidColorBrush)view.FindControl<Border>("DeeperWebcamConsentCard")!.BorderBrush!).Color);
            Assert.Equal(Loc.Get("blink_trainer_calibration_none"), vm.CalibrationStatusText);

            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = "outdated";
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.ShowRevokeConsent); // stale consent must not look granted
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("blink_trainer_consent_granted"), vm.ConsentStatusText);
            Assert.True(view.FindControl<Button>("BtnDeeperWebcamRevokeConsent")!.IsVisible);
            Assert.Equal(Color.Parse("#4ADE80"), ((ISolidColorBrush)view.FindControl<Border>("DeeperWebcamConsentCard")!.BorderBrush!).Color);

            WebcamTracker.Instance.Calibration = new WebcamCalibrationData();
            view.RefreshWebcamColumn();
            Assert.Equal(Loc.Get("blink_trainer_calibration_outdated"), vm.CalibrationStatusText);
            WebcamTracker.Instance.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DeviceName = "Screen 2" } };
            view.RefreshWebcamColumn();
            Assert.Equal(Loc.GetF("blink_trainer_calibration_calibrated_format", "Screen 2"), vm.CalibrationStatusText);
        }
        finally
        {
            host?.Close();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.Instance.Calibration) = old;
        }
    });

    [Fact]
    public void CardButtonsReachSharedDialogsWithoutStartingCamera() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.Instance.Calibration);
        MainShellWindow? shell = null;
        try
        {
            s.WebcamConsentGiven = false;
            WebcamTracker.Instance.Calibration = null;
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            foreach (var name in new[] { "BtnDeeperWebcamManageConsent", "BtnDeeperWebcamStartStopTracker", "BtnDeeperWebcamQuickRecal" })
            {
                Click(view, name);
                var consent = Assert.Single(shell.OwnedWindows.OfType<WebcamConsentDialog>());
                consent.Close(false);
                Dispatcher.UIThread.RunJobs();
                Assert.False(WebcamTracker.Instance.IsRunning);
            }
            Click(view, "BtnDeeperWebcamCalibrate");
            var calibration = Assert.Single(shell.OwnedWindows.OfType<WebcamCalibrationWindow>());
            Assert.Equal("Webcam tracking is not running. Start tracking before calibrating.", calibration.FindControl<TextBlock>("TxtErrorDetail")!.Text);
            calibration.Close(false);
            Dispatcher.UIThread.RunJobs();

            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            Click(view, "BtnDeeperWebcamQuickRecal");
            var message = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("blink_trainer_quick_recal_needs_full_body"), message.FindControl<TextBlock>("TxtMessage")!.Text);
            message.Close(false);
            Dispatcher.UIThread.RunJobs();
            Click(view, "BtnDeeperWebcamRevokeConsent");
            var confirm = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.True(confirm.FindControl<Button>("BtnCancel")!.IsDefault);
            confirm.Close(false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(WebcamConsent.IsCurrent(s));
            Click(view, "BtnDeeperDeviceSettings");
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Equal("devices", shell.Named<AppSettingsTabView>("AppSettingsTab")!.CheckedSectionKey);
        }
        finally
        {
            if (shell != null) { foreach (var dialog in shell.OwnedWindows.ToList()) dialog.Close(); shell.Close(); }
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.Instance.Calibration) = old;
        }
    });
}
