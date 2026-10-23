using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ShellCalibrationNotificationTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("display-1", false)]
    [InlineData(" ", false)] // WPF IsNullOrEmpty, not IsNullOrWhiteSpace
    public void LegacyNotice_uses_exact_Wpf_condition_copy_action_and_key(string? device, bool expected) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var notices = new NotificationService { RemoveLater = (a, _) => a() };
        var host = new StackPanel();
        notices.AttachHost(host);
        int opened = 0;
        var calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DeviceName = device } };
        MainShellWindow.OfferLegacyCalibrationNotice(null, notices, () => opened++);
        MainShellWindow.OfferLegacyCalibrationNotice(new WebcamCalibrationData(), notices, () => opened++);
        Assert.Empty(host.Children);
        MainShellWindow.OfferLegacyCalibrationNotice(calibration, notices, () => opened++);
        MainShellWindow.OfferLegacyCalibrationNotice(calibration, notices, () => opened++);
        if (!expected) { Assert.Empty(host.Children); return; }
        var toast = Assert.IsType<Border>(Assert.Single(host.Children));
        Assert.Null(toast.Tag); // indefinite
        Assert.Equal(Color.Parse("#FFB347"), Assert.IsType<SolidColorBrush>(toast.BorderBrush).Color);
        Assert.Contains(toast.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Your calibration needs updating for multi-monitor support.");
        var action = toast.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ToastAction");
        Assert.Equal("Recalibrate", Assert.IsType<TextBlock>(action.Content).Text);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, opened);
        Assert.Empty(host.Children);
        Assert.DoesNotContain("recalibrate-multimonitor", CoreSettings.Current.DismissedNotificationKeys);
        MainShellWindow.OfferLegacyCalibrationNotice(calibration, notices, () => opened++);
        notices.Dismiss("recalibrate-multimonitor");
        Assert.Empty(host.Children); // same stable key as the calibration success path
    });

    [Fact]
    public void PlayerLevelAward_reaches_the_notice_and_sound_caller() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var oldSink = OsNotifications.Sink;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        settings.Current.OfflineUsername = "tester";
        settings.Current.PlayerLevel = 1;
        settings.Current.PlayerXP = 0;
        var seen = new System.Collections.Generic.List<string>();
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { seen.Add("sound"); return () => { }; };
        OsNotifications.Sink = (title, _) => seen.Add(title == Loc.Get("toast_level_up_title") ? "player-notice" : "other-notice");
        var shell = new MainShellWindow();
        try
        {
            ProgressionBank.Add(XpCurve.GetXPForLevel(1, XpCurve.EpochOf(settings.Current)), "ShellNotificationTest");
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.Current.PlayerLevel > 1);
            int notice = seen.IndexOf("player-notice");
            Assert.True(notice >= 0);
            Assert.Equal("sound", seen[notice + 1]);
        }
        finally
        {
            shell.Close();
            MainShellWindow.StopLevelUpSound();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.PlayStoppableProvider = oldAudio;
            OsNotifications.Sink = oldSink;
            CoreSettings.SaveImmediate();
        }
    });

    [Fact]
    public void ShellOpen_checks_the_tracker_and_close_stops_the_level_sound() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var tracker = WebcamTracker.Instance;
        var oldCalibration = tracker.Calibration;
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var notices = AvApp.Notifications;
        var oldRemove = notices.RemoveLater;
        var host = new StackPanel();
        notices.AttachHost(host);
        notices.RemoveLater = (a, _) => a();
        int stopped = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => stopped++;
        var shell = new MainShellWindow();
        try
        {
            tracker.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord() };
            shell.Show();
            Assert.Contains(host.Children, c => c.GetLogicalDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "Your calibration needs updating for multi-monitor support."));
            MainShellWindow.PlayLevelUpSound();
            shell.Close();
            Assert.Equal(1, stopped);
        }
        finally
        {
            shell.Close();
            MainShellWindow.StopLevelUpSound();
            tracker.Calibration = oldCalibration;
            CoreAudio.PlayStoppableProvider = oldAudio;
            notices.Dismiss("recalibrate-multimonitor");
            notices.RemoveLater = oldRemove;
            notices.AttachHost(new StackPanel());
        }
    });
}
