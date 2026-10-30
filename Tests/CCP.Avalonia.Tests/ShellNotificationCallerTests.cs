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
    public void PanicRetiresCalibrationBeforeCameraWithoutStartingCamera()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var window = new WebcamCalibrationWindow();
            var closed = false;
            window.Closed += (_, _) => closed = true;
            var calibration = PanicSurfaces.All.Single(s => s.Id == "webcam-calibration");
            Assert.Contains("webcam-calibration", PanicSurfaces.All.Single(s => s.Id == "camera").After!);
            calibration.Stop(null);
            Assert.True(closed);
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

    [Fact]
    public void WallpaperFolderFallbackDurationAndRecoveryActionsMatchWpf()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.MediaSource = "local";
            var root = Path.Combine(CorePaths.UserData, "library-" + Guid.NewGuid().ToString("N"));
            CorePaths.EffectiveAssetsProvider = () => root;
            var shell = new MainShellWindow();
            var host = FreshToastHost();
            try
            {
                var custom = Path.Combine(root, "custom");
                Directory.CreateDirectory(custom);
                File.WriteAllText(Path.Combine(custom, "image.PNG"), "fixture");
                settings.Current.WallpaperSourceFolder = custom;
                shell.WarnIfWallpaperLibraryEmpty();
                Assert.Empty(host.Children);
                settings.Current.WallpaperSourceFolder = Path.Combine(root, "missing");
                shell.WarnIfWallpaperLibraryEmpty();
                var toast = Assert.IsType<Border>(Assert.Single(host.Children));
                Assert.Equal(TimeSpan.FromSeconds(18), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
                var buttons = toast.GetLogicalDescendants().OfType<Button>().ToArray();
                Click(buttons.Single(b => b.Name == "ToastAction"));
                Assert.Equal("assets", shell.CurrentTab);
                shell.WarnIfWallpaperLibraryEmpty();
                var latest = host.Children.Last();
                string? opened = null;
                ExternalOpener.Shell = target => { opened = target; return true; };
                Click(latest.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ToastSecondaryAction"));
                Assert.Null(opened); // The suite sandbox refuses remote browser launches.
                var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(TestSource())!, "..", "..",
                    "CCP.Avalonia", "Views", "Windows", "MainShellWindow.Notifications.cs"));
                Assert.Contains("ExternalOpener.Open(DiscordLinks.PackCatalogue)", File.ReadAllText(source));
            }
            finally
            {
                shell.RequestExit();
                shell.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
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

    private static string TestSource([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void StartButtonWarnsOnAnEmptyFlashLibraryExceptInAudioOnlySessions() => WithShell(null, (shell, settings, host, _) =>
    {
        settings.Current.FlashEnabled = true;
        var beforeSystemStart = host.Children.Count;
        shell.StartEngine(); // The lockdown/program/remote path must not stack library nags.
        Assert.True(CoreEngine.IsRunning);
        Assert.Equal(beforeSystemStart, host.Children.Count);
        MainShellWindow.StopEngine();
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

}
