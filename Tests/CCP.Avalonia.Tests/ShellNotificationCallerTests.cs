using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ShellNotificationCallerTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void PanicRetiresCalibrationWithoutStartingCamera()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var window = new WebcamCalibrationWindow();
            var closed = false;
            window.Closed += (_, _) => closed = true;
            PanicSurfaces.All.Single(s => s.Id == "webcam-calibration").Stop(null);
            Assert.True(closed);
        });
    }

    [Fact]
    public void LegacyMonitorPromptIsStickyAndAbsentForNamedMonitors()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var tracker = WebcamTracker.Instance;
            var before = tracker.Calibration;
            var host = new StackPanel();
            App.Notifications.AttachHost(host);
            var shell = new MainShellWindow();
            try
            {
                tracker.SetCalibrationLive(new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DeviceName = "fixture-monitor" } });
                shell.CheckLegacyCalibration();
                Assert.Empty(host.Children);
                tracker.SetCalibrationLive(new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord() });
                shell.CheckLegacyCalibration();
                shell.CheckLegacyCalibration();
                Assert.Single(host.Children);
                Assert.Contains(host.Children[0].GetLogicalDescendants().OfType<Button>(), b => (b.Content as TextBlock)?.Text == "Recalibrate");
            }
            finally
            {
                App.Notifications.Dismiss("recalibrate-multimonitor");
                tracker.SetCalibrationLive(before);
                shell.Close();
            }
        });
    }

    [Fact]
    public void LevelNoticesUseWpfCopyAndCompanionMilestones()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var notices = new List<(string, string)>();
            var old = OsNotifications.Sink;
            OsNotifications.Sink = (a, b) => notices.Add((a, b));
            try
            {
                MainShellWindow.NotifyPlayerLevel(12);
                MainShellWindow.NotifyCompanionLevel((CompanionId)0, 9);
                Assert.Single(notices);
                Assert.Equal((Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", 12)), notices[0]);
                MainShellWindow.NotifyCompanionLevel((CompanionId)0, 10);
                Assert.Equal("Companion Level Up!", notices[1].Item1);
                Assert.EndsWith(" reached Level 10!", notices[1].Item2);
                MainShellWindow.NotifyCompanionLevel((CompanionId)0, CompanionProgress.MaxLevel);
                Assert.Equal("MAX LEVEL!", notices[2].Item1);
                Assert.EndsWith(" has reached maximum level!", notices[2].Item2);
            }
            finally { OsNotifications.Sink = old; }
        });
    }

    [Fact]
    public void EmptyLocalLibrariesHaveTwoActionsAndOnlineOrDisabledFlashesDoNotNag()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldAssets = CorePaths.EffectiveAssetsProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var root = Path.Combine(CorePaths.UserData, "notification-empty-" + Guid.NewGuid().ToString("N"));
            CorePaths.EffectiveAssetsProvider = () => root;
            var host = new StackPanel();
            App.Notifications.AttachHost(host);
            var shell = new MainShellWindow();
            try
            {
                settings.Current.MediaSource = "local";
                settings.Current.FlashEnabled = false;
                shell.WarnIfFlashLibraryEmpty();
                Assert.Empty(host.Children);
                settings.Current.FlashEnabled = true;
                settings.Current.MediaSource = "mixed";
                shell.WarnIfFlashLibraryEmpty();
                shell.WarnIfWallpaperLibraryEmpty();
                Assert.Empty(host.Children);
                settings.Current.MediaSource = "local";
                shell.WarnIfFlashLibraryEmpty();
                shell.WarnIfWallpaperLibraryEmpty();
                Assert.Equal(2, host.Children.Count);
                foreach (var toast in host.Children)
                {
                    var buttons = toast.GetLogicalDescendants().OfType<Button>().ToArray();
                    Assert.Contains(buttons, b => (b.Content as TextBlock)?.Text == Loc.Get("btn_open_media_manager"));
                    Assert.Contains(buttons, b => (b.Content as TextBlock)?.Text == Loc.Get("btn_get_packs"));
                }
                Directory.CreateDirectory(Path.Combine(root, "images"));
                File.WriteAllText(Path.Combine(root, "images", "fixture.PNG"), "fixture");
                shell.WarnIfFlashLibraryEmpty();
                Assert.Equal(2, host.Children.Count);
            }
            finally
            {
                shell.Close();
                CoreSettings.ServiceProvider = oldSettings;
                CorePaths.EffectiveAssetsProvider = oldAssets;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            Dispatcher.UIThread.RunJobs();
        });
    }
}
