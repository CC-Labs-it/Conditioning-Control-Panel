using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
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

    /// <summary>P02: toasts shown before any host existed (another test started the engine headless) queue
    /// and flush into the first host attached; drain them into a throwaway host so this test sees only its own.</summary>
    private static StackPanel FreshToastHost()
    {
        App.Notifications.AttachHost(new StackPanel());
        var host = new StackPanel();
        App.Notifications.AttachHost(host);
        return host;
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
            var host = FreshToastHost();
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
            var host = FreshToastHost();
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
                    Assert.Contains(buttons, b => (b.Content as TextBlock)?.Text == global::ConditioningControlPanel.Avalonia.Controls.IconText.BareLoc("btn_get_packs"));
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

    /// <summary>P04: a real shell from its constructor, an empty local library, OS notices and toasts captured.</summary>
    private static void WithShell(Action<SettingsService>? before, Action<MainShellWindow, SettingsService, StackPanel, List<(string Title, string Body)>> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldAssets = CorePaths.EffectiveAssetsProvider;
            var oldSink = OsNotifications.Sink;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            foreach (var k in FeatureIntros.All.Keys) s.SeenFeatureIntros.Add(k);   // P52/P76
            s.WhatMovedCardShown = 1;
            s.PanicKeyEnabled = false;   // StartEffect runs synchronously
            s.MediaSource = "local";
            var root = Path.Combine(CorePaths.UserData, "notification-shell-" + Guid.NewGuid().ToString("N"));
            CorePaths.EffectiveAssetsProvider = () => root;
            var notices = new List<(string Title, string Body)>();
            OsNotifications.Sink = (a, b) => notices.Add((a, b));
            var host = FreshToastHost();
            MainShellWindow? shell = null;
            try
            {
                before?.Invoke(settings);
                shell = new MainShellWindow();
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell, settings, host, notices);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                shell?.RequestExit();
                shell?.Close();
                Dispatcher.UIThread.RunJobs();
                SettingsService.DisarmAllPendingSaves();
                OsNotifications.Sink = oldSink;
                CoreSettings.ServiceProvider = oldSettings;
                CorePaths.EffectiveAssetsProvider = oldAssets;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void StartButtonWarnsOnAnEmptyFlashLibraryExceptInAudioOnlySessions() => WithShell(null, (shell, settings, host, _) =>
    {
        settings.Current.FlashEnabled = true;
        settings.Current.AudioOnlySession = true;   // WPF StartStop.cs:345: no flashes, no flash nag
        var start = shell.Named<Button>("BtnStart")!;
        var toasts = host.Children.Count;
        Click(start);
        Assert.True(CoreEngine.IsRunning);
        Assert.Equal(toasts, host.Children.Count);
        Click(start);
        Assert.False(CoreEngine.IsRunning);
        settings.Current.AudioOnlySession = false;
        Click(start);
        Assert.True(CoreEngine.IsRunning);
        Assert.Equal(toasts + 1, host.Children.Count);
    });

    [Fact]
    public void KeepWallpaperCheckboxWarnsOnAnEmptyWallpaperLibrary() => WithShell(null, (shell, settings, host, _) =>
    {
        settings.Current.WallpaperEnabled = false;
        shell.ShowTab("bambitakeover");
        Dispatcher.UIThread.RunJobs();
        var tab = shell.Named<BambiTakeoverTabView>("BambiTakeoverTab")!;
        var toasts = host.Children.Count;
        tab.ChkWallpaperKeep.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.Current.WallpaperEnabled);
        Assert.Equal(toasts + 1, host.Children.Count);
    });

    [Fact]
    public void ScheduleTickHidesToTrayStartsAndStopsWithWpfNotices() => WithShell(null, (shell, settings, _, notices) =>
    {
        var s = settings.Current;
        (s.SchedulerEnabled, s.SchedulerMonday, s.SchedulerStartTime, s.SchedulerEndTime) = (true, true, "16:00", "22:00");
        shell.CreateTray();
        var monday = new DateTime(2026, 10, 26, 17, 0, 0);
        shell.ScheduleTick(monday);
        Assert.True(CoreEngine.IsRunning);
        Assert.False(shell.IsVisible);   // WPF: minimised to the tray
        Assert.True(shell.Tray!.IsVisible);
        Assert.Equal<(string, string)>(new[] { (Loc.Get("app_title"), Loc.Get("tray_balloon_body")), ("Scheduler Active", "Session auto-started based on schedule.") }, notices.ToArray());
        shell.ScheduleTick(monday.AddHours(6));
        Assert.False(CoreEngine.IsRunning);
        Assert.Equal(("Scheduler", "Scheduled session ended."), notices[^1]);
        Assert.Equal(3, notices.Count);
    });

    [Fact]
    public void RampCompletionNotifiesStopsAndRestoresTheBases() => WithShell(null, (shell, settings, _, notices) =>
    {
        var s = settings.Current;
        (s.IntensityRampEnabled, s.RampDurationMinutes, s.RampMode, s.RampStartPercent, s.RampEndPercent) = (true, 1, RampMode.Range, 100, 200);
        (s.RampLinkFlashOpacity, s.EndSessionOnRampComplete, s.FlashOpacity) = (true, true, 30);
        var atNotice = -1;
        OsNotifications.Sink = (a, b) => { notices.Add((a, b)); atNotice = s.FlashOpacity; };
        Click(shell.Named<Button>("BtnStart")!);
        Assert.True(CoreEngine.IsRunning);
        shell.ShellRampTick(DateTime.Now.AddMinutes(s.RampDurationMinutes + 1));
        Assert.Equal(("Session Complete", "Intensity ramp finished. Stopping..."), notices.Single());
        Assert.Equal(60, atNotice);   // ramped when it finished
        Assert.False(CoreEngine.IsRunning);
        Assert.Equal(30, s.FlashOpacity);   // engine stop handed the base back
    });

    [Fact]
    public void ShellLaunchHealsACrashedRampAndSavesIt()
    {
        var journal = Path.Combine(CorePaths.UserData, "ramp_restore.json");
        WithShell(settings =>
        {
            settings.Current.FlashOpacity = 99;
            File.WriteAllText(journal, "{\"at\":0,\"fields\":{\"FlashOpacity\":31}}");
            SettingsService.DisarmAllPendingSaves();
            Assert.False(settings.HasPendingSave);
        }, (_, settings, _, _) =>
        {
            Assert.Equal(31, settings.Current.FlashOpacity);
            Assert.False(File.Exists(journal));
            Assert.True(settings.HasPendingSave);   // WPF App.xaml.cs:2540-2541 saves the heal
        });
    }

    [Fact]
    public void LevelUpSubscriptionsPostNoticesAndDetachOnClose()
    {
        // Only the handlers the shell constructor added are raised: other subscribers (achievements) stay quiet.
        static Delegate[] Handlers(Type t) =>
            ((Delegate?)t.GetField("LevelUp", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();
        Delegate[] players = Array.Empty<Delegate>(), companions = Array.Empty<Delegate>();
        WithShell(_ => (players, companions) = (Handlers(typeof(ProgressionBank)), Handlers(typeof(CompanionCore))), (shell, _, _, notices) =>
    {
        static bool Ours(Delegate d) => d.Method.Name.Contains("InitializeShellNotifications", StringComparison.Ordinal);
        var player = Assert.Single(Handlers(typeof(ProgressionBank)).Except(players), Ours);
        var companion = Assert.Single(Handlers(typeof(CompanionCore)).Except(companions), Ours);
        player.DynamicInvoke(7);
        companion.DynamicInvoke((CompanionId)0, 10);
        Assert.Empty(notices);   // posted, like WPF's Dispatcher.Invoke from the XP thread
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.GetF("toast_level_up_body", 7), notices[0].Body);
        Assert.Equal("Companion Level Up!", notices[1].Title);
        player.DynamicInvoke(8);
        shell.RequestExit();
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, notices.Count);   // queued before close, dropped after
        Assert.DoesNotContain(player, Handlers(typeof(ProgressionBank)));
        Assert.DoesNotContain(companion, Handlers(typeof(CompanionCore)));
    });
    }
}
