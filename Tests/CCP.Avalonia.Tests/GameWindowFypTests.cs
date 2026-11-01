using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Webcam;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The For You feed on the shared game host (WPF Services/Fyp/FypHostService.cs): ShowTab("fyp") opens it
/// behind OpenFypFeed's premium gate, panic closes it, and the page's frames run the Core FypRules. No network:
/// the remote batch and the sub probe are seams.</summary>
public sealed class GameWindowFypTests
{
    private static void Run(Action<MainShellWindow, string> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;   // P52/P76
            var root = Directory.CreateTempSubdirectory("fyp-host-").FullName;
            var lib = Directory.CreateDirectory(Path.Combine(root, "lib")).FullName;
            var server = new WebAssetServer(root);
            var (oldServer, oldPremium, oldDenied, oldAssets, oldXp) = (GameWindow.Server, CoreEntitlement.HasPremiumProvider,
                CoreEntitlement.ShowDeniedHandler, CorePaths.EffectiveAssetsProvider, CoreProgression.AddXPProvider);
            GameWindow.Server = () => server;
            CorePaths.EffectiveAssetsProvider = () => lib;
            var shell = new MainShellWindow();
            try
            {
                // SetupWithoutStarting skips the desktop startup's toast-host attachment (App.axaml.cs:945).
                global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(
                    shell.FindControl<Panel>("NotificationHost")!);
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell, lib);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(new StackPanel());
                (GameWindow.Server, CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (oldServer, oldPremium, oldDenied);
                (CorePaths.EffectiveAssetsProvider, CoreProgression.AddXPProvider) = (oldAssets, oldXp);
                CoreSettings.ServiceProvider = null;
                server.Dispose();
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void Visible_feed_stands_down_mandatory_video_and_panic_releases_the_screen() => Run((shell, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var host = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var video = new MandatoryVideoScheduler(host, library: () => new[] { "/v/a.mp4" });
        Assert.False(host.FeedOwnsTheScreen);
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.True(host.FeedOwnsTheScreen);
        Assert.False(video.Trigger());
        Assert.False(video.IsPlaying);
        var oldVideo = CoreEngine.Video;
        try
        {
            CoreEngine.Video = video;
            var card = new ConditioningControlPanel.Avalonia.Views.Features.VideoFeatureControl();
            card.FindControl<Button>("BtnTestVideo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(card.TestVideoRun.IsCompletedSuccessfully);
            var notice = shell.FindControl<StackPanel>("NotificationHost")!.Children.OfType<Border>().Last();
            Assert.Contains(notice.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == ConditioningControlPanel.Localization.Loc.Get("video_toast_feed_has_the_screen"));
            Assert.Equal(TimeSpan.FromSeconds(6), Assert.IsType<DispatcherTimer>(notice.Tag).Interval);
        }
        finally { CoreEngine.Video = oldVideo; video.Stop(); } // also cancels the pending poll
        GameWindow.CloseAllForPanic();
        Assert.False(host.FeedOwnsTheScreen);
    });

    /// <summary>WPF ShowTab("fyp") -> OpenFypFeed (MainWindow.Lab.cs:234), refused out loud; panic closes it (GameSurfaces.cs:56).</summary>
    [Fact]
    public void ShowTabFyp_IsGatedOpensTheFeed_AndPanicClosesIt() => Run((shell, _) =>
    {
        var denied = 0;
        CoreEntitlement.ShowDeniedHandler = _ => denied++;
        CoreEntitlement.HasPremiumProvider = () => false;
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Equal(1, denied);
        CoreEntitlement.HasPremiumProvider = () => true;
        var score = EmiSuggester.ScoreOf("fyp");
        var tab = shell.CurrentTab;
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.True(GameWindow.IsAnyOpen());
        var w = GameWindow.Launch(GameWindow.FypId);   // focuses the live one
        Assert.NotNull(w);
        Assert.Equal(GameWindow.FypId, w!.Spec.Id);
        Assert.EndsWith("/fyp/index.html", w.PageUrl!.AbsolutePath);
        Assert.Equal(tab, shell.CurrentTab);                 // a window, not a tab
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.True(EmiSuggester.ScoreOf("fyp") > score);   // WPF Launch NoteOpen("fyp")
        // The EMI door opens the feed itself (WPF EmiTargets.cs:215), not the panel.
        shell.Hide();
        ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiTargets.Find("fyp")!.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.True(GameWindow.IsAnyOpen());
        Assert.False(shell.IsVisible);
    });

    /// <summary>WPF OnPageMessage: init on this window's asset gate, WPF's own ghost/eye refusals, clip XP.</summary>
    [Fact]
    public void Frames_InitRefusalsAndXp() => Run((_, lib) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        Directory.CreateDirectory(Path.Combine(lib, "videos"));
        File.WriteAllBytes(Path.Combine(lib, "videos", "clip.mp4"), new byte[] { 1 });
        CoreSettings.Current.FypEyeControl = false;
        CoreSettings.Current.WebcamConsentGiven = false;
        var xp = new List<double>();
        CoreProgression.AddXPProvider = (amount, _) => xp.Add(amount);
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        var init = sent.Single();
        Assert.Equal("init", (string?)init["type"]);
        Assert.False((bool)init["settings"]!["eyeControl"]!);
        Assert.EndsWith("/ccp.assets/videos/clip.mp4", (string?)init["assets"]!.Single()["url"]);
        Assert.Equal(new Uri((string)init["assets"]![0]!["url"]!).Authority, w.PageUrl!.Authority);
        sent.Clear();
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
        Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, sent.Select(o => (string?)o["type"]));
        Assert.False((bool)sent[0]["on"]!);
        sent.Clear();
        w.FypConsentOverride = () => Task.FromResult(false);
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Wait(w.FypEyeTask!);
        Assert.False(CoreSettings.Current.FypEyeControl);
        Assert.Equal("eyeStatus", (string?)sent.Single()["type"]);
        Assert.False((bool)sent[0]["enabled"]!);
        Assert.Equal("consent", (string?)sent[0]["reason"]);
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"muted\",\"value\":true}");
        Assert.True(CoreSettings.Current.FypMuted);
        w.HandleMessage("{\"type\":\"clip-viewed\",\"segId\":\"videos/clip.mp4\"}");
        w.HandleMessage("{\"type\":\"attention-hit\"}");
        Assert.Equal(new[] { 5.0, 15.0 }, xp);
    });

    /// <summary>WPF ServeRemoteBatch / ProbeCustomSub: no media without consent; a found sub is committed.</summary>
    [Fact]
    public void RemoteBatchNeedsConsent_ProbeCommits() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        int fetches = 0;
        w.FypFetchOverride = _ =>
        {
            fetches++;
            var entry = new ConditioningControlPanel.Services.Fyp.FypAssetManifest.Entry { Id = "scrolller/a/1", Url = "https://cdn.invalid/1.mp4" };
            return Task.FromResult(new FypOnlineCoordinator.FeedBatch(new() { entry }, null, false, 7));
        };
        w.FypProbeOverride = (_, _) => Task.FromResult(new SubProbe { Ok = true, VideoCount = 9 });
        Wait(w.ServeFypRemoteBatch());
        Assert.Equal(0, fetches);                            // no consent: never asked
        var s = CoreSettings.Current;
        s.FypOnlineConsented = true;
        s.FypSource = "online";
        Wait(w.ServeFypRemoteBatch());
        Assert.Equal(1, fetches);
        Assert.Equal(new[] { "assets-append", "online-status" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal(7, (int)sent[1]["poolTotal"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"probe-sub\",\"sub\":\"hypnofeed\"}");
        Wait(w.FypLastProbe!);
        Assert.Equal(new[] { "sub-probe", "library" }, sent.Select(o => (string?)o["type"]));
        Assert.Contains("hypnofeed", s.FypOnlineCustomSubs);
    });

    [Fact]
    public void FileMenu_LocalClipOpensOrReveals_UnsafeAndMissingIdsAreRefused() => Run((_, lib) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var video = Directory.CreateDirectory(Path.Combine(lib, "videos")).FullName;
        var path = Path.Combine(video, "clip.mp4");
        File.WriteAllBytes(path, new byte[] { 1 });
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var targets = new List<string>();
        var oldShell = ExternalOpener.Shell;
        var oldSelect = ExternalOpener.SelectInFileManager;
        ExternalOpener.Shell = target => { targets.Add(target); return true; };
        ExternalOpener.SelectInFileManager = _ => false; // exercise the existing folder fallback
        try
        {
            w.HandleMessage("{\"type\":\"file-menu\",\"id\":\"videos/clip.mp4\"}");
            Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsType<ContextMenu>(w.FypFileMenu);
            var items = menu.Items.Cast<MenuItem>().ToArray();
            Assert.Equal(new[] { ConditioningControlPanel.Localization.Loc.Get("fyp_menu_open_file"),
                ConditioningControlPanel.Localization.Loc.Get("fyp_menu_show_in_folder") }, items.Select(x => x.Header));
            // Optional visual evidence of the real popup, which render-all's default window sample cannot show.
            if (Environment.GetEnvironmentVariable("CCP_FYP_MENU_RENDER") is { Length: > 0 } proof)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(
                    (int)Math.Ceiling(menu.Bounds.Width), (int)Math.Ceiling(menu.Bounds.Height)));
                bitmap.Render(menu);
                bitmap.Save(proof);
            }
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { path, video }, targets);
            File.Delete(path);
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(2, targets.Count); // deleted between menu creation and click
            foreach (var id in new[] { "videos/missing.mp4", "videos/run.exe", "../outside.mp4", "scrolller/a/1" })
            {
                w.HandleMessage(new JObject { ["type"] = "file-menu", ["id"] = id }.ToString());
                Dispatcher.UIThread.RunJobs();
                Assert.Null(w.FypFileMenu);
            }
            w.HandleMessage("{\"type\":\"file-menu\",\"id\":\"videos/clip.mp4\"}");
            Assert.Null(w.FypFileMenu);
            File.WriteAllBytes(path, new byte[] { 1 });
            w.HandleMessage("{\"type\":\"file-menu\",\"id\":\"videos/clip.mp4\"}");
            var lastMenu = Assert.IsType<ContextMenu>(w.FypFileMenu);
            Assert.True(lastMenu.IsOpen);
            GameWindow.CloseAllForPanic();
            Assert.False(lastMenu.IsOpen);
            Assert.Null(w.FypFileMenu);
        }
        finally { ExternalOpener.Shell = oldShell; ExternalOpener.SelectInFileManager = oldSelect; }
    });

    [Fact]
    public void FileMenu_RevealSelectsTheFile_AndHandlesMissingFileOrFileManager() => Run((_, lib) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var dir = Directory.CreateDirectory(Path.Combine(lib, "videos")).FullName;
        var path = Path.Combine(dir, "a clip.mp4");
        File.WriteAllBytes(path, new byte[] { 1 });
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var selected = new List<string>();
        var opened = new List<string>();
        var (oldShell, oldSelect) = (ExternalOpener.Shell, ExternalOpener.SelectInFileManager);
        var selects = true;
        ExternalOpener.Shell = target => { opened.Add(target); return true; };
        ExternalOpener.SelectInFileManager = target => { selected.Add(target); return selects; };
        try
        {
            w.HandleMessage("{\"type\":\"file-menu\",\"id\":\"videos/a clip.mp4\"}");
            var menu = Assert.IsType<ContextMenu>(w.FypFileMenu);
            var reveal = menu.Items.Cast<MenuItem>().Last();
            reveal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { path }, selected);
            Assert.Empty(opened);

            selects = false; // FileManager1 is unavailable
            reveal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { path, path }, selected);
            Assert.Equal(new[] { dir }, opened);

            File.Delete(path); // it disappeared after the menu opened, as in WPF's fallback
            reveal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { path, path }, selected);
            Assert.Equal(new[] { dir, dir }, opened);
        }
        finally { (ExternalOpener.Shell, ExternalOpener.SelectInFileManager) = (oldShell, oldSelect); }
    });

    [Fact]
    public void EyeControl_ConsentCameraGesturesGazeCalibrationAndCleanup() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var settings = CoreSettings.Current;
        settings.WebcamConsentGiven = false;
        settings.FypEyeControl = false;
        settings.FypEyeGaze = true;
        var tracker = WebcamTracker.Instance;
        var oldCalibration = tracker.Calibration;
        tracker.Calibration = null;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        int prompts = 0, starts = 0, stops = 0, calibrations = 0;
        bool running = false;
        w.FypConsentOverride = () => { prompts++; return Task.FromResult(true); };
        w.FypCameraRunningOverride = () => running;
        w.FypStartCameraOverride = () => { starts++; running = true; return Task.FromResult(true); };
        w.FypStopCameraOverride = () => { stops++; running = false; };
        w.FypCalibrationOverride = () => { calibrations++; tracker.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DeviceName = "test", X = 100, Y = 200, DpiScale = 2 } }; return Task.CompletedTask; };
        try
        {
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
            Wait(w.FypEyeTask!);
            Assert.Equal(1, prompts);
            Assert.Equal(1, starts);
            Assert.Equal("starting", (string?)sent[0]["reason"]);
            Assert.True((bool)sent.Last()["running"]!);
            Assert.False((bool)sent.Last()["calibrated"]!);
            sent.Clear();
            tracker.RaiseBlinkForTest();
            tracker.RaiseEyesClosedLongForTest();
            tracker.RaiseGazeForTest(new Point(10, 10)); // no calibration yet
            Assert.Equal(new[] { "blink", "eyesClosed" }, sent.Select(x => (string?)x["type"]));
            sent.Clear();
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Wait(w.FypCalibrationTask!);
            Assert.Equal(1, calibrations);
            Assert.True((bool)sent.Last()["calibrated"]!);
            sent.Clear();
            var origin = w.Web.PointToScreen(new Point(0, 0));
            tracker.RaiseGazeForTest(new Point((origin.X + 10 - 100) / 2.0, (origin.Y + 10 - 200) / 2.0));
            tracker.RaiseGazeForTest(new Point((origin.X + 20 - 100) / 2.0, (origin.Y + 20 - 200) / 2.0));
            var gaze = Assert.Single(sent);
            Assert.Equal("gaze", (string?)gaze["type"]);
            Assert.True((bool)gaze["inside"]!);
            Assert.Equal(10 / w.RenderScaling / w.Web.Bounds.Width, (double)gaze["x"]!, 5);
            sent.Clear();
            System.Threading.Thread.Sleep(120);
            tracker.RaiseGazeForTest(new Point((origin.X - 200) / 2.0, (origin.Y - 300) / 2.0));
            Assert.False((bool)sent.Single()["inside"]!);
            Assert.Equal(0, (double)sent.Single()["x"]!);
            sent.Clear();
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeGaze\",\"value\":false}");
            sent.Clear();
            System.Threading.Thread.Sleep(120);
            tracker.RaiseGazeForTest(new Point(10, 10));
            Assert.Empty(sent);
            GameWindow.CloseAllForPanic();
            Assert.Equal(1, stops);
            sent.Clear();
            tracker.RaiseBlinkForTest();
            tracker.RaiseEyesClosedLongForTest();
            Assert.Empty(sent);
        }
        finally { tracker.Calibration = oldCalibration; }
    });

    [Fact]
    public void EyeControl_PreservesExistingCamera_AndReleasesLateStartAfterClose() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current;
        s.WebcamConsentGiven = true;
        s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        s.FypEyeControl = true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        bool running = true;
        int starts = 0, stops = 0;
        w.FypCameraRunningOverride = () => running;
        w.FypStartCameraOverride = () => { starts++; return Task.FromResult(true); };
        w.FypStopCameraOverride = () => { stops++; running = false; };
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.True((bool)sent.Single()["settings"]!["eyeControl"]!);
        Wait(w.FypEyeTask!);
        Assert.Equal(0, starts);
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
        Assert.Equal(0, stops); // the tracker was already running
        running = false;
        var pending = new TaskCompletionSource<bool>();
        w.FypStartCameraOverride = () => { starts++; return pending.Task; };
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, starts);
        GameWindow.CloseAllForPanic();
        running = true;
        pending.SetResult(true);
        Wait(w.FypEyeTask!);
        Assert.Equal(1, stops);
        Assert.False(running);
    });

    [Fact]
    public void EyeControl_ClosingConsentBeforeFinishingDoesNotStartCamera() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current;
        s.WebcamConsentGiven = false;
        s.FypEyeControl = false;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        int starts = 0;
        w.FypCameraRunningOverride = () => false;
        w.FypStartCameraOverride = () => { starts++; return Task.FromResult(true); };
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(w.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.WebcamConsentDialog>());
        var next = dialog.FindControl<Button>("BtnNext")!;
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach (var name in new[] { "ChkConsent1", "ChkConsent2", "ChkConsent3" })
            dialog.FindControl<CheckBox>(name)!.IsChecked = true;
        dialog.FindControl<TextBox>("TxtConfirm")!.Text = "ENABLE";
        dialog.FindControl<Button>("BtnEnable")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(dialog.ConsentGiven);
        dialog.Close(); // window close before choosing a calibration option is not a successful dialog
        Wait(w.FypEyeTask!);
        Assert.Equal(0, starts);
        Assert.False(s.FypEyeControl);
        Assert.Equal("consent", (string?)sent.Last()["reason"]);
    });

    [Fact]
    public void EyeControl_ToggleOffDuringConsentNeverStartsCamera() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current;
        s.WebcamConsentGiven = false;
        s.FypEyeControl = false;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var consent = new TaskCompletionSource<bool>();
        int starts = 0;
        w.FypConsentOverride = () => consent.Task;
        w.FypStartCameraOverride = () => { starts++; return Task.FromResult(true); };
        w.FypCameraRunningOverride = () => false;
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        var task = w.FypEyeTask!;
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
        consent.SetResult(true);
        Wait(task);
        Assert.Equal(0, starts);
        Assert.False(s.FypEyeControl);
    });

    [Fact]
    public void EyeControl_OffThenOnWhileStarting_RetriesAfterReleasingFirstStart() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current;
        s.WebcamConsentGiven = true;
        s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        s.FypEyeControl = false;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        bool running = false;
        int starts = 0, stops = 0;
        var pending = new TaskCompletionSource<bool>();
        w.FypCameraRunningOverride = () => running;
        w.FypStartCameraOverride = () => ++starts == 1 ? pending.Task : Task.FromResult(running = true);
        w.FypStopCameraOverride = () => { stops++; running = false; };
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        var first = w.FypEyeTask!;
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        running = true;
        pending.SetResult(true);
        Wait(first);
        Wait(w.FypEyeTask!);
        Assert.Equal(2, starts);
        Assert.Equal(1, stops);
        Assert.True(running);
        GameWindow.CloseAllForPanic();
        Assert.Equal(2, stops);
    });

    [Fact]
    public void EyeControl_CameraFailureResetsToggle_AndCalibrationRequiresRunningCamera() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current;
        s.WebcamConsentGiven = true;
        s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
        s.FypEyeControl = false;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.FypCameraRunningOverride = () => false;
        w.FypStartCameraOverride = () => Task.FromResult(false);
        int calibrations = 0;
        w.FypCalibrationOverride = () => { calibrations++; return Task.CompletedTask; };
        w.HandleMessage("{\"type\":\"calibrate\"}");
        Wait(w.FypCalibrationTask!);
        Assert.Equal(0, calibrations);
        Assert.False((bool)sent.Last()["running"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Wait(w.FypEyeTask!);
        Assert.False(s.FypEyeControl);
        Assert.Equal("no-camera", (string?)sent.Last()["reason"]);
    });

    private static void Wait(Task t)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!t.IsCompleted && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        Assert.True(t.IsCompletedSuccessfully);
        Dispatcher.UIThread.RunJobs();
    }
}
