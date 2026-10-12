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

    private sealed class FakeHttp : System.Net.Http.HttpMessageHandler
    {
        public System.Net.Http.HttpRequestMessage? Seen;
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            Seen = request;
            return System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("{\"ok\":1}") });
        }
    }

    /// <summary>WPF PieceByPieceHostService through the launcher tile: the leash gate first, then Play opens the
    /// board; ready gets pbp:settings then pbp:identity and no shell init (:295-313); media-request answers from
    /// the own library with gifs split out (:447); pbp:net is refused off /v2/pbp/ and otherwise carries the token
    /// host-side (:559); a silent page gets one ping then closes (:717); panic closes the board.</summary>
    [Fact]
    public void PieceByPiece_LeashThenPlay_FramesNetWatchdogAndPanic() => Run((shell, launcher) =>
    {
        var lib = Directory.CreateTempSubdirectory("pbp-lib-").FullName;
        var (oldAssets, oldHttp, oldBase) = (CorePaths.EffectiveAssetsProvider, GameWindow.PbpHttpClient, GameWindow.PbpServerBase);
        var oldSecret = CoreSecrets.RetrieveProvider;
        var oldLoopback = Environment.GetEnvironmentVariable(FriendsHead.EnvVar);
        var fake = new FakeHttp();
        using var http = new System.Net.Http.HttpClient(fake);
        try
        {
            // The sandboxed head honours the loopback override like App's FriendsHead.Create (WPF has no sandbox).
            Environment.SetEnvironmentVariable(FriendsHead.EnvVar, "http://127.0.0.1:9");
            Assert.Equal("http://127.0.0.1:9", oldBase());
            Directory.CreateDirectory(Path.Combine(lib, "images"));
            Directory.CreateDirectory(Path.Combine(lib, "videos"));
            File.WriteAllBytes(Path.Combine(lib, "images", "spin.gif"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "images", "still.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "videos", "clip.mp4"), new byte[] { 1 });
            CorePaths.EffectiveAssetsProvider = () => lib;
            GameWindow.PbpHttpClient = () => http;                     // no network in tests
            GameWindow.PbpServerBase = () => "http://pbp.test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;   // P02: never the keyring
            launcher.Hide();
            var card = LauncherCards.Find(GameWindow.PbpId)!;
            Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == GameWindow.PbpId);

            int gates = 0;
            MainShellWindow.LeashGateDueProvider = () => true;
            MainShellWindow.PresentLeashGateProvider = _ => gates++;
            launcher.Play(card);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, gates);
            Assert.False(GameWindow.IsAnyOpen());
            MainShellWindow.LeashGateDueProvider = () => false;

            launcher.Play(card);
            Dispatcher.UIThread.RunJobs();
            var w = GameWindow.Launch(GameWindow.PbpId)!;            // the open board, focused
            Assert.Contains("piecebypiece/index.html", w.PageUrl!.AbsolutePath);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnNavigationCompleted(w.PageUrl);
            w.Web.OnWebMessage("\"{\\\"type\\\":\\\"ready\\\"}\"");   // the string carrier's JSON-quoted frame
            Assert.Equal(new[] { "pbp:settings", "pbp:identity" }, sent.Select(o => (string?)o["type"]));
            Assert.Equal(15, (int)sent[0]["videoHoldSec"]!);
            Assert.Equal("tok", (string?)sent[1]["net"]!["authToken"]);

            w.Web.OnWebMessage("{\"type\":\"pbp:media-request\",\"kinds\":[\"gif\",\"video\"],\"count\":8}");
            Assert.True(w.PbpPending.Wait(TimeSpan.FromSeconds(10)));   // off the UI thread; the frame is queued when it ends
            Dispatcher.UIThread.RunJobs();
            var media = sent.Last();
            Assert.Equal("pbp:media", (string?)media["type"]);
            Assert.Empty(media["images"]!);
            Assert.EndsWith("/ccp.assets/images/spin.gif", (string?)media["gifs"]!.Single());
            Assert.EndsWith("/ccp.assets/videos/clip.mp4", (string?)media["videos"]!.Single());
            // Walked once per window, like the Breakout deal: a file added mid-game is not re-scanned.
            File.WriteAllBytes(Path.Combine(lib, "images", "late.gif"), new byte[] { 1 });
            w.Web.OnWebMessage("{\"type\":\"pbp:media-request\",\"kinds\":[\"gif\"],\"count\":8}");
            Assert.True(w.PbpPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.EndsWith("/ccp.assets/images/spin.gif", (string?)sent.Last()["gifs"]!.Single());

            w.Web.OnWebMessage("{\"type\":\"pbp:net\",\"id\":\"n1\",\"method\":\"GET\",\"path\":\"/v2/stakes/x\"}");
            Assert.Equal("forbidden_path", (string?)sent.Last()["body"]);
            Assert.Null(fake.Seen);
            w.Web.OnWebMessage("{\"type\":\"pbp:net\",\"id\":\"n2\",\"method\":\"POST\",\"path\":\"/v2/pbp/lobby\",\"body\":{\"a\":1}}");
            Assert.True(w.PbpPending.Wait(TimeSpan.FromSeconds(10)));   // the net reply's own completion signal (P57)
            Dispatcher.UIThread.RunJobs();
            var result = sent.Single(o => (string?)o["id"] == "n2");
            Assert.Equal(200, (int)result["status"]!);
            Assert.Equal("http://pbp.test/v2/pbp/lobby", fake.Seen!.RequestUri!.ToString());
            Assert.Equal("tok", fake.Seen.Headers.GetValues("X-Auth-Token").Single());

            var now = DateTime.UtcNow;
            Assert.Equal("ok", w.CheckPbpWatch(now));
            Assert.Equal("ping", w.CheckPbpWatch(now.AddSeconds(25)));
            Assert.Equal("ping", (string?)sent.Last()["type"]);
            w.Web.OnWebMessage("{\"type\":\"pong\"}");
            Assert.Equal("ok", w.CheckPbpWatch(DateTime.UtcNow));
            Assert.Equal("ping", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(25)));
            Assert.Equal("closed", w.CheckPbpWatch(DateTime.UtcNow.AddSeconds(30)));
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());

            launcher.Play(card);                                       // reopened: panic closes it
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally
        {
            (CorePaths.EffectiveAssetsProvider, GameWindow.PbpHttpClient, GameWindow.PbpServerBase) = (oldAssets, oldHttp, oldBase);
            CoreSecrets.RetrieveProvider = oldSecret;
            Environment.SetEnvironmentVariable(FriendsHead.EnvVar, oldLoopback);
            Directory.Delete(lib, true);
        }
    });
}
