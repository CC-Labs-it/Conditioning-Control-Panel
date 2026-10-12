using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shared game host (WPF LauncherHost.LaunchGame -> BreakoutHostService.LaunchDemo ->
/// ChaosWebViewHost): the launcher's Play opens it after the leash check, the page protocol answers
/// ready with init and media-request with a deal, exit-done closes it, and the panic registry closes it.</summary>
public sealed class GameWindowTests
{
    private static void Run(Action<MainShellWindow, LauncherWindow> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;   // P52/P76
            s.LauncherSoundEnabled = false;
            var root = Directory.CreateTempSubdirectory("game-host-").FullName;
            var server = new WebAssetServer(root);
            var (oldServer, oldIn, oldAudio) = (GameWindow.Server, CoreAccount.IsLoggedInProvider, CoreAudio.PlayOneShotProvider);
            var (oldDue, oldGate) = (MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider);
            GameWindow.Server = () => server;
            CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
            CoreAccount.IsLoggedInProvider = () => false;    // the demo needs no account
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(LauncherWindow.BackToLauncher(shell));
                Dispatcher.UIThread.RunJobs();
                body(shell, LauncherWindow.Instance!);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                LauncherWindow.Instance?.Close();
                shell.Close();
                (GameWindow.Server, CoreAccount.IsLoggedInProvider, CoreAudio.PlayOneShotProvider) = (oldServer, oldIn, oldAudio);
                (MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider) = (oldDue, oldGate);
                CoreSettings.ServiceProvider = null;
                server.Dispose();
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void Play_OpensTheGameHost_AndPanicClosesIt() => Run((shell, launcher) =>
    {
        Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "breakoutdemo");
        launcher.Hide();                                  // off screen: no exit beat to wait out
        launcher.Play(LauncherCards.Find("breakoutdemo")!);
        Dispatcher.UIThread.RunJobs();
        Assert.True(GameWindow.IsAnyOpen());
        Assert.False(shell.IsVisible);                    // a game, not the panel (WPF LauncherHost.LaunchGame)
        var games = PanicSurfaces.All.Single(x => x.Id == "games");
        Assert.True(games.OwnsTheScreen!());              // P30: a panic that ends it skips the exit ladder
        games.Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
    });

    /// <summary>WPF LauncherHost.cs:360-366: a pending leash punishment sends the tile to the gate, not the game.</summary>
    [Fact]
    public void Play_UnderALeashPunishment_GoesToTheGate_NotTheGame() => Run((_, launcher) =>
    {
        int gates = 0;
        MainShellWindow.LeashGateDueProvider = () => true;
        MainShellWindow.PresentLeashGateProvider = _ => gates++;
        launcher.Hide();
        launcher.Play(LauncherCards.Find("breakoutdemo")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, gates);
        Assert.False(GameWindow.IsAnyOpen());
    });

    /// <summary>Through the web view's own carrier (WebHost.OnWebMessage, from the loaded page only): ready gets
    /// init with the 3-level demo access (WPF BackRoomHostService.BuildInit :566), a station's media-request gets
    /// the library's gifs (WPF BackRoomBridge.cs:208), exit-done closes.</summary>
    [Fact]
    public void PageFrames_ThroughTheWebView_InitMediaAndExit() => Run((_, _) =>
    {
        var lib = Directory.CreateTempSubdirectory("game-lib-").FullName;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        try
        {
            Directory.CreateDirectory(Path.Combine(lib, "images"));
            File.WriteAllBytes(Path.Combine(lib, "images", "spin.gif"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "images", "still.png"), new byte[] { 1 });   // not an animation
            CorePaths.EffectiveAssetsProvider = () => lib;
            var w = GameWindow.Launch("breakoutdemo")!;
            Assert.Same(w, GameWindow.Launch("breakoutdemo"));   // focused, never relaunched
            Assert.Contains("backroom/stations/breakout/play.html", w.PageUrl!.AbsolutePath);
            Assert.Contains("desktop=1&demo=1", w.PageUrl.Query);   // 3 levels even before init (access.js)
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnWebMessage("{\"type\":\"ready\"}");          // before the page loaded: dropped
            Assert.Empty(sent);
            w.Web.OnNavigationCompleted(w.PageUrl);
            w.Web.OnWebMessage("\"{\\\"type\\\":\\\"ready\\\"}\"");   // invokeCSharpAction's JSON-quoted string
            Assert.Equal(new[] { "init" }, sent.Select(o => (string?)o["type"]));
            Assert.True((bool)sent[0]["breakoutStandalone"]!);
            Assert.Equal(3, (int)sent[0]["breakout"]!["storyLimit"]!);
            w.Web.OnWebMessage("{\"type\":\"media-request\",\"reqId\":\"r1\",\"station\":\"breakout\",\"count\":8}");
            var until = DateTime.UtcNow.AddSeconds(10);
            while (sent.Count < 2 && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
            var media = sent[1];
            Assert.Equal("media", (string?)media["type"]);
            Assert.Equal("r1", (string?)media["reqId"]);
            Assert.EndsWith("/ccp.assets/images/spin.gif", (string?)media["gifs"]!.Single()["url"]);
            // Walked once per window (WPF's FlashService snapshot): a file added mid-game is not re-scanned.
            File.WriteAllBytes(Path.Combine(lib, "images", "late.gif"), new byte[] { 1 });
            w.Web.OnWebMessage("{\"type\":\"media-request\",\"reqId\":\"r2\",\"station\":\"breakout\",\"count\":8}");
            until = DateTime.UtcNow.AddSeconds(10);
            while (sent.Count < 3 && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
            Assert.EndsWith("/ccp.assets/images/spin.gif", (string?)sent[2]["gifs"]!.Single()["url"]);
            // WPF BackRoomBridge.OnFx: every fx gets its fx-ack at once, or the page waits out 4 s per effect.
            w.Web.OnWebMessage("{\"type\":\"fx\",\"token\":\"t7\",\"fxId\":\"fx.gif_storm\",\"station\":\"breakout\",\"symbols\":[]}");
            var ack = sent.Last();
            Assert.Equal("fx-ack", (string?)ack["type"]);
            Assert.Equal("t7", (string?)ack["token"]);
            Assert.Empty(ack["fired"]!);
            Assert.Equal("unknown", (string?)ack["skipped"]!.Single()["why"]);
            w.Web.OnWebMessage("{\"type\":\"exit-done\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { CorePaths.EffectiveAssetsProvider = oldAssets; Directory.Delete(lib, true); }
    });

    /// <summary>WPF DtrhAssetManifest.Build's local half: images/ and videos/, deselected files out,
    /// dot-folders out, empty or foreign media counted as skipped.</summary>
    [Fact]
    public void Manifest_WalksTheLibrary()
    {
        var root = Directory.CreateTempSubdirectory("game-manifest-").FullName;
        try
        {
            void Put(string rel, int bytes)
            {
                var p = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, new byte[bytes]);
            }
            Put("images/a.png", 4);
            Put("images/sub/b.jpg", 4);
            Put("images/off.png", 4);
            Put("images/.hidden/c.png", 4);
            Put("images/empty.png", 0);
            Put("images/d.bmp", 4);
            Put("videos/v.mp4", 4);
            var m = GameMediaManifest.Build(root, rel => "u/" + rel, new[] { "images\\off.png" });
            Assert.Equal(new[] { "a.png", "b.jpg" }, m.Images.Select(e => e.Name).OrderBy(x => x));
            Assert.Contains(m.Images, e => e.Url == "u/images/sub/b.jpg");
            Assert.Equal("v.mp4", m.Videos.Single().Name);
            Assert.Equal(2, m.Skipped);   // the empty png and the bmp
            Assert.False(m.Truncated);
            Assert.Empty(GameMediaManifest.Build(null, rel => rel, null).Images);
            // A media deal reads pictures only: BuildLocal never walks videos/.
            var oldAssets = CorePaths.EffectiveAssetsProvider;
            CorePaths.EffectiveAssetsProvider = () => root;
            using var server = new WebAssetServer(root);
            try { Assert.Empty(GameMediaManifest.BuildLocal(server).Videos); }
            finally { CorePaths.EffectiveAssetsProvider = oldAssets; }
        }
        finally { Directory.Delete(root, true); }
    }

    // ---- Racing Thoughts (WPF Services/Chaos/CaucusHostService.cs) ----

    /// <summary>Signed in, owning <paramref name="owned"/> (null = nothing); XP and Sparks go to lists.</summary>
    private static void RunRace(string? owned, Action<LauncherWindow, List<double>, List<double>> body) => Run((_, launcher) =>
    {
        var (oldOwns, oldBank, oldXp, oldAssets) = (RacingAccess.IsGrantedProvider, GameWindow.RaceBankSparks, CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider);
        var lib = Directory.CreateTempSubdirectory("race-lib-").FullName;
        var xp = new List<double>();
        var sparks = new List<double>();
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            RacingAccess.IsGrantedProvider = id => id == owned;
            GameWindow.RaceBankSparks = input => { sparks.Add(input.Score); return 7; };
            CoreProgression.AddXPProvider = (amount, _) => xp.Add(amount);
            CorePaths.EffectiveAssetsProvider = () => lib;
            body(launcher, xp, sparks);
        }
        finally
        {
            (RacingAccess.IsGrantedProvider, GameWindow.RaceBankSparks, CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider) = (oldOwns, oldBank, oldXp, oldAssets);
            Directory.Delete(lib, true);
        }
    });

    /// <summary>WPF CaucusHostService.Launch :132 + LauncherCatalogue :234: no track, no race (and no tile here,
    /// where WPF draws the mystery card); an owned track opens it from the launcher after the leash check; panic closes it.</summary>
    [Fact]
    public void Race_DoorAndLauncher_LeashGate_AndPanic()
    {
        RunRace(null, (launcher, _, _) =>
        {
            Assert.DoesNotContain(LauncherWindow.VisibleCards, c => c.Id == "race");
            Assert.Null(GameWindow.Launch("race"));
            Assert.False(GameWindow.IsAnyOpen());
        });
        RunRace("rt.original.03", (launcher, _, _) =>
        {
            Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "race");
            launcher.Hide();
            int gates = 0;
            MainShellWindow.LeashGateDueProvider = () => true;
            MainShellWindow.PresentLeashGateProvider = _ => gates++;
            launcher.Play(LauncherCards.Find("race")!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, gates);
            Assert.False(GameWindow.IsAnyOpen());
            MainShellWindow.LeashGateDueProvider = () => false;
            launcher.Play(LauncherCards.Find("race")!);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(MainShellWindow.Current!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        });
    }

    /// <summary>WPF OnPageReady :255 (init with the race settings block, then manifest) and OnRunEnded :431:
    /// one payout per run, score / 5 under 250 XP a minute, Sparks banked on score / 5.</summary>
    [Fact]
    public void Race_BootAndOnePayoutPerRun() => RunRace("rt.original.03", (_, xp, sparks) =>
    {
        var w = GameWindow.Launch("race")!;
        Assert.Contains("dtrh/race.html", w.PageUrl!.AbsolutePath);
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(new[] { "init", "manifest" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal(new[] { 3 }, sent[0]["settings"]!["racingTracks"]!.Values<int>());
        Assert.False((bool)sent[0]["settings"]!["cloud"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"run-ended\",\"score\":5000,\"durationSec\":60}");   // no run-started: nothing
        Assert.Empty(sent);
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.True(w.RaceRunActive);
        w.HandleMessage("{\"type\":\"run-ended\",\"score\":5000,\"durationSec\":60}");
        w.HandleMessage("{\"type\":\"run-ended\",\"score\":5000,\"durationSec\":60}");   // a second end pays nothing
        var pay = Assert.Single(sent);
        Assert.Equal("payout-result", (string?)pay["type"]);
        Assert.Equal(250, (int)pay["baseXp"]!);                 // min(5000 / 5, 250 * 1 min)
        Assert.Equal(7, (int)pay["sparksEarned"]!);
        Assert.Equal(new[] { 250.0 }, xp);
        Assert.Equal(new[] { 1000.0 }, sparks);
        Assert.Equal((40, 40), GameWindow.RacePayout(200, 60, 1.0));
    });

    /// <summary>WPF StartHeartbeatWatch :595: idle until ready; silent past 20 s on the menu (10 s mid-run)
    /// gets one ping, a pong resets it, still silent on the next tick closes the window.</summary>
    [Fact]
    public void Race_HeartbeatWatchdog_PingsThenCloses() => RunRace("rt.original.00", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        Assert.True(w.RaceWatchRunning);                // WPF Launch arms the watch
        var t0 = DateTime.UtcNow;
        Assert.Equal("idle", w.CheckRaceHeartbeat(t0.AddSeconds(60)));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("ok", w.CheckRaceHeartbeat(t0.AddSeconds(15)));
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.Equal("pinged", w.CheckRaceHeartbeat(t0.AddSeconds(15)));
        w.HandleMessage("{\"type\":\"pong\"}");
        Assert.Equal("ok", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(5)));
        Assert.Equal("pinged", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(30)));
        Assert.Equal("closed", w.CheckRaceHeartbeat(DateTime.UtcNow.AddSeconds(35)));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.RaceWatchRunning);               // DisposeAll stops it (P65)
    });
}
