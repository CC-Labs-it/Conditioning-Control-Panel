using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;
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
            {
                // Same portable font setup as QuestsDepthTests; keep real Skia text measurement.
                var fonts = AppFonts.Options();
                var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
                mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(AppFonts.Inter);
                fonts.FontFamilyMappings = mappings;
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            }
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
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell, lib);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                (GameWindow.Server, CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (oldServer, oldPremium, oldDenied);
                (CorePaths.EffectiveAssetsProvider, CoreProgression.AddXPProvider) = (oldAssets, oldXp);
                CoreSettings.ServiceProvider = null;
                server.Dispose();
                Directory.Delete(root, true);
            }
        });
    }

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
        CoreSettings.Current.FypEyeControl = true;
        var xp = new List<double>();
        CoreProgression.AddXPProvider = (amount, _) => xp.Add(amount);
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.FypConsent = () => Task.FromResult(false);   // the saved eye setting re-arms on ready; refuse it here
        w.HandleMessage("{\"type\":\"ready\"}");
        Wait(w.FypEyeTask!);
        var init = sent[0];
        Assert.Equal("init", (string?)init["type"]);
        Assert.True((bool)init["settings"]!["eyeControl"]!);
        Assert.EndsWith("/ccp.assets/videos/clip.mp4", (string?)init["assets"]!.Single()["url"]);
        Assert.Equal(new Uri((string)init["assets"]![0]!["url"]!).Authority, w.PageUrl!.Authority);
        sent.Clear();
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
        Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, sent.Select(o => (string?)o["type"]));
        Assert.False((bool)sent[0]["on"]!);
        sent.Clear();
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
        ExternalOpener.Shell = target => { targets.Add(target); return true; };
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
        finally { ExternalOpener.Shell = oldShell; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Eyes_ConsentGesturesCalibrationAndPanic_OnlyStopOwnedCamera(bool alreadyRunning) => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        bool running = alreadyRunning;
        int starts = 0, stops = 0, calibrations = 0;
        w.FypConsent = () => Task.FromResult(true);
        w.FypCameraRunning = () => running;
        w.FypStartCamera = () => { starts++; running = true; return Task.FromResult(true); };
        w.FypStopCamera = () => { stops++; running = false; return Task.CompletedTask; };
        w.FypCalibration = () => { calibrations++; return Task.CompletedTask; };
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Wait(w.FypEyeTask!);
        Assert.Equal(alreadyRunning ? 0 : 1, starts);
        Assert.True((bool)sent.Last()["running"]!);
        WebcamTracker.Instance.RaiseBlinkForTest();
        Assert.Equal("blink", (string?)sent.Last()["type"]);
        w.FypEyesClosed();
        Assert.Equal("eyesClosed", (string?)sent.Last()["type"]);
        w.HandleMessage("{\"type\":\"calibrate\"}");
        Wait(w.FypEyeTask!);
        Assert.Equal(1, calibrations);
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(null);
        Assert.Equal(alreadyRunning ? 0 : 1, stops);
        var count = sent.Count;
        WebcamTracker.Instance.RaiseBlinkForTest();
        w.FypEyesClosed();
        Assert.Equal(count, sent.Count);
        CoreSettings.Current.FypEyeControl = false;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Eyes_ReenableWaitsForDelayedStop_EvenAfterReopen(bool reopen) => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var stop = new TaskCompletionSource<bool>();
        bool running = false;
        int starts = 0, stops = 0;
        async Task StopCamera()
        {
            stops++;
            await stop.Task;
            running = false;
        }
        void Configure(GameWindow window)
        {
            window.FypConsent = () => Task.FromResult(true);
            window.FypCameraRunning = () => running;
            window.FypStartCamera = () => { starts++; running = true; return Task.FromResult(true); };
            window.FypStopCamera = StopCamera;
        }
        Configure(w);
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Wait(w.FypEyeTask!);
        try
        {
            if (reopen)
            {
                w.Close();
                w = GameWindow.Launch(GameWindow.FypId)!;
                Configure(w);
            }
            else w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
            Assert.Equal(1, stops);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.FypEyeTask!.IsCompleted); // the old camera still reports running
            WebcamTracker.Instance.RaiseBlinkForTest();
            Assert.DoesNotContain(sent, frame => (string?)frame["type"] == "blink");
            Assert.Equal(1, starts);
            stop.SetResult(true);
            Wait(w.FypEyeTask!);
            Assert.Equal(2, starts);
            Assert.True(running);
            WebcamTracker.Instance.RaiseBlinkForTest();
            Assert.Equal("blink", (string?)sent.Last()["type"]);
        }
        finally
        {
            stop.TrySetResult(true);
            Wait(w.FypEyeTask!);
            w.Close();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Eyes_ReopenDuringStartup_WaitsForStartupAndCleanup(bool firstStarts) => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var first = new TaskCompletionSource<bool>();
        var stop = new TaskCompletionSource<bool>();
        bool busy = false, running = false;
        int starts = 0, stops = 0;
        async Task<bool> StartCamera()
        {
            starts++;
            if (busy) return false; // WebcamTracker.Start refuses a second start while busy.
            if (starts == 1)
            {
                busy = true;
                try { running = await first.Task; }
                finally { busy = false; }
            }
            else running = true;
            return running;
        }
        async Task StopCamera()
        {
            stops++;
            await stop.Task;
            running = false;
        }
        void Configure(GameWindow window)
        {
            window.FypConsent = () => Task.FromResult(true);
            window.FypCameraRunning = () => running;
            window.FypStartCamera = StartCamera;
            window.FypStopCamera = StopCamera;
        }
        var old = GameWindow.Launch(GameWindow.FypId)!;
        Configure(old);
        old.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        Assert.True(busy);
        old.Close();
        var current = GameWindow.Launch(GameWindow.FypId)!;
        Configure(current);
        var sent = new List<JObject>();
        current.Posted += json => sent.Add(JObject.Parse(json));
        current.HandleMessage("{\"type\":\"ready\"}"); // saved eye setting re-arms the new visit
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, starts);
            Assert.True(CoreSettings.Current.FypEyeControl);
            Assert.False(current.FypEyeTask!.IsCompleted);
            first.SetResult(firstStarts);
            Dispatcher.UIThread.RunJobs();
            if (firstStarts)
            {
                Assert.Equal(1, stops);
                Assert.Equal(1, starts);
                Assert.False(current.FypEyeTask.IsCompleted);
            }
            stop.SetResult(true);
            Wait(old.FypEyeTask!);
            Wait(current.FypEyeTask);
            Assert.Equal(2, starts);
            Assert.Equal(firstStarts ? 1 : 0, stops);
            Assert.True(running);
            Assert.True(CoreSettings.Current.FypEyeControl);
            sent.Clear();
            WebcamTracker.Instance.RaiseBlinkForTest();
            Assert.Equal("blink", (string?)Assert.Single(sent)["type"]);
        }
        finally
        {
            first.TrySetResult(firstStarts);
            stop.TrySetResult(true);
            Wait(old.FypEyeTask!);
            Wait(current.FypEyeTask!);
            current.Close();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Eyes_ClosingOrDisablingDuringCameraStart_ReleasesLateCamera(bool close) => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var start = new TaskCompletionSource<bool>();
        int stops = 0;
        w.FypConsent = () => Task.FromResult(true);
        w.FypCameraRunning = () => false;
        w.FypStartCamera = () => start.Task;
        w.FypStopCamera = () => { stops++; return Task.CompletedTask; };
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        if (close) w.Close();
        else w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
        start.SetResult(true);
        Wait(w.FypEyeTask!);
        Assert.Equal(1, stops);
        CoreSettings.Current.FypEyeControl = false;
    });

    [Fact]
    public void Eyes_ReenableDuringLateCameraCleanup_IsNotLost() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var start = new TaskCompletionSource<bool>();
        var stop = new TaskCompletionSource<bool>();
        bool running = false;
        int starts = 0;
        w.FypConsent = () => Task.FromResult(true);
        w.FypCameraRunning = () => running;
        w.FypStartCamera = async () => { starts++; running = starts > 1 || await start.Task; return running; };
        w.FypStopCamera = async () => { await stop.Task; running = false; };
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        var first = w.FypEyeTask!;
        try
        {
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
            start.SetResult(true);                 // late start: the cleanup stop is now pending
            Dispatcher.UIThread.RunJobs();
            Assert.False(first.IsCompleted);
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
            stop.SetResult(true);
            Wait(first);
            Wait(w.FypEyeTask!);
            Assert.Equal(2, starts);
            Assert.True(running);
            Assert.True(CoreSettings.Current.FypEyeControl);
            sent.Clear();
            WebcamTracker.Instance.RaiseBlinkForTest();
            Assert.Equal("blink", (string?)Assert.Single(sent)["type"]);
        }
        finally
        {
            start.TrySetResult(true);
            stop.TrySetResult(true);
            Wait(first);
            Wait(w.FypEyeTask!);
            w.Close();
            CoreSettings.Current.FypEyeControl = false;
        }
    });

    [Fact]
    public void Eyes_StartFailureResetsSetting_AndCalibrationNeedsCamera() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        w.FypConsent = () => Task.FromResult(true);
        w.FypCameraRunning = () => false;
        w.FypStartCamera = () => Task.FromResult(false);
        int calibrations = 0;
        w.FypCalibration = () => { calibrations++; return Task.CompletedTask; };
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Wait(w.FypEyeTask!);
        Assert.False(CoreSettings.Current.FypEyeControl);
        Assert.Equal("starting", (string?)sent.First()["reason"]);
        Assert.Equal("no-camera", (string?)sent.Last()["reason"]);
        w.HandleMessage("{\"type\":\"calibrate\"}");
        Wait(w.FypEyeTask!);
        Assert.Equal(0, calibrations);
    });

    [Fact]
    public void Eyes_GazeNeedsModeAndCalibration_AndCoalescesToTenHz() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        w.FypConsent = () => Task.FromResult(true);
        w.FypCameraRunning = () => true;
        var now = DateTime.UtcNow;
        w.FypEyeClock = () => now;
        var camera = WebcamTracker.Instance;
        var before = camera.Calibration;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        try
        {
            camera.Calibration = null;
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
            Wait(w.FypEyeTask!);
            sent.Clear();
            CoreSettings.Current.FypEyeGaze = true;
            w.FypGaze(new Point(10, 10));
            Assert.Empty(sent);
            camera.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord() };
            w.FypGaze(new Point(10, 10));
            w.FypGaze(new Point(20, 20));
            Assert.Single(sent);
            now = now.AddMilliseconds(100);
            w.FypGaze(new Point(-10000, -10000));
            Assert.Equal(2, sent.Count);
            Assert.False((bool)sent.Last()["inside"]!);
            Assert.Equal(0, (double)sent.Last()["x"]!);
            CoreSettings.Current.FypEyeGaze = false;
            now = now.AddMilliseconds(100);
            w.FypGaze(new Point(20, 20));
            Assert.Equal(2, sent.Count);
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":false}");
            sent.Clear();
            WebcamTracker.Instance.RaiseBlinkForTest();
            Assert.Empty(sent);
        }
        finally { camera.Calibration = before; CoreSettings.Current.FypEyeControl = false; }
    });

    [Fact]
    public void Eyes_SavedSettingRearmsOnReady_WithoutDuplicateGestures() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        w.FypConsent = () => Task.FromResult(true);
        w.FypCameraRunning = () => true;
        CoreSettings.Current.FypEyeControl = true;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Wait(w.FypEyeTask!);
        Assert.True((bool)sent.First()["settings"]!["eyeControl"]!);
        w.HandleMessage("{\"type\":\"ready\"}");
        Wait(w.FypEyeTask!);
        sent.Clear();
        WebcamTracker.Instance.RaiseBlinkForTest();
        Assert.Single(sent);
        Assert.Equal("blink", (string?)sent.Single()["type"]);
        CoreSettings.Current.FypEyeControl = false;
    });

    private static void Wait(Task t)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!t.IsCompleted && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        Assert.True(t.IsCompletedSuccessfully);
        Dispatcher.UIThread.RunJobs();
    }
}
