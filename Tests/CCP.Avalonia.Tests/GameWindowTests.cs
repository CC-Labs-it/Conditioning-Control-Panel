using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services.Webcam;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shared game host (WPF LauncherHost.LaunchGame -> BreakoutHostService.LaunchDemo ->
/// ChaosWebViewHost): the launcher's Play opens it after the leash check, the page protocol answers
/// ready with init and media-request with a deal, exit-done closes it, and the panic registry closes it.</summary>
public sealed partial class GameWindowTests
{
    private static void Run(Action<MainShellWindow, LauncherWindow> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
                shell.StepLauncherFade(1); // with motion on the door first fades the panel (WPF MainWindow.Launcher.cs)
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

    /// <summary>WPF ChaosWebViewHost.cs:222-225,1704: query/fragment changes are the same document;
    /// a different path, origin or scheme cannot drive the game bridge.</summary>
    [Fact]
    public void PageFrames_OnlyTheLoadedDocumentCanDriveTheBridge() => AvaloniaTestDispatcher.Run(() =>
    {
        var root = Directory.CreateTempSubdirectory("game-document-").FullName;
        using var server = new WebAssetServer(root);
        var w = new GameWindow(GameWindow.Games["breakoutdemo"]);
        try
        {
            w.Load(server);
            w.Show();
            var sent = new List<string>();
            w.Posted += sent.Add;
            var page = w.PageUrl!;
            foreach (var other in new[]
            {
                new Uri(page, "/intake/index.html"),
                new UriBuilder(page) { Port = page.Port + 1 }.Uri,
                new UriBuilder(page) { Scheme = "https" }.Uri,
            })
            {
                w.Web.OnNavigationCompleted(other);
                w.Web.OnWebMessage("{\"type\":\"ready\"}");
                w.Web.OnWebMessage("{\"type\":\"exit-done\"}");
                Assert.Empty(sent);
                Assert.False(w.IsReady);
                Assert.Contains(w, GameWindow.OpenWindowsForTests());
            }
            w.Web.OnNavigationCompleted(new UriBuilder(page) { Query = "changed=1", Fragment = "dock" }.Uri);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.True(w.IsReady);
            Assert.Equal("init", (string?)JObject.Parse(Assert.Single(sent))["type"]);
            w.Web.OnWebMessage("{\"type\":\"exit-done\"}");
            Assert.DoesNotContain(w, GameWindow.OpenWindowsForTests());
        }
        finally { w.Close(); Directory.Delete(root, true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Breakout_PanicLadder_ClosesOnSecondPressOrResumes(bool resume) => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        try
        {
            s.PanicKey = "F8"; s.PanicKeyEnabled = true; s.PanicOverridesAll = false;
            s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            launcher.Hide();
            var w = GameWindow.Launch("breakoutdemo")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            Assert.True(tracker.Start(), tracker.LastError);
            var now = DateTime.Now;
            shell.HandlePanicKeyPress(now);
            Assert.True(w.IsVisible);
            Assert.True(SpinWait.SpinUntil(() => !tracker.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.True(WebcamConsent.IsCurrent(s));
            var frame = Assert.Single(sent, x => (string?)x["type"] == "suspend");
            Assert.True((bool)frame["on"]!);
            Assert.Equal("panic", (string?)frame["reason"]);
            Assert.Equal(0, shell.PanicPressCountForTest);
            if (resume)
            {
                w.ResumeBreakoutAfterPanic(now.AddSeconds(2));
                Assert.Single(sent);
                w.ResumeBreakoutAfterPanic(now.AddSeconds(3));
                Assert.False((bool)sent.Last()["on"]!);
                Assert.Equal("panic", (string?)sent.Last()["reason"]);
                Assert.True(w.IsVisible);
            }
            else
            {
                Assert.True(tracker.Start(), tracker.LastError); // manually restarted after press 1
                shell.HandlePanicKeyPress(now.AddSeconds(1));
                Assert.False(w.IsVisible);
                Assert.True(SpinWait.SpinUntil(() => !tracker.IsRunning, TimeSpan.FromSeconds(5)));
                Assert.Equal(0, shell.PanicPressCountForTest);
            }
        }
        finally
        {
            tracker.Stop();
            SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(20));
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void Breakout_OverridePanicClosesImmediately() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "F8"; s.PanicKeyEnabled = true; s.PanicOverridesAll = true;
        launcher.Hide();
        var w = GameWindow.Launch("breakoutdemo")!;
        shell.HandlePanicKeyPress(DateTime.Now);
        Assert.False(w.IsVisible);
        Assert.Equal(0, shell.PanicPressCountForTest);
    });

    [Theory]
    [InlineData("breakoutdemo")]
    [InlineData("breakout")]
    public void Breakout_ReadyIncludesCurrentPlans(string id) => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.Games[id]);
        try
        {
            var sent = new List<string>();
            w.Posted += sent.Add;
            w.HandleMessage("{\"type\":\"ready\"}");
            var plans = JObject.Parse(Assert.Single(sent))["plans"];
            Assert.True(JToken.DeepEquals(
                ConditioningControlPanel.Services.Vault.PlanCatalog.ToWire(ConditioningControlPanel.Services.Vault.PlanCatalog.Current), plans));
        }
        finally { w.Close(); }
    });

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
            w.RainBridgeFactory = () => new ConditioningControlPanel.Avalonia.Views.BackRoom.BackRoomRainBridge(
                (a, b, c, d, e, f) => { }, () => { }, () => false, () => false, () => 100);
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
            Assert.Equal("gif-rain", (string?)ack["fired"]!.Single());
            Assert.All(ack["skipped"]!, skip => Assert.Equal("unknown", (string?)skip["why"]));
            w.Web.OnWebMessage("{\"type\":\"fx\",\"token\":\"t8\",\"fxId\":\"fx.unknown\",\"station\":\"breakout\"}");
            var unknown = sent.Last();
            Assert.Equal("t8", (string?)unknown["token"]);
            Assert.Empty(unknown["fired"]!);
            Assert.Equal("unknown", (string?)unknown["skipped"]!.Single()["why"]);
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
            try { Assert.Empty(GameMediaManifest.BuildLocal(server.AssetUrl).Videos); }
            finally { CorePaths.EffectiveAssetsProvider = oldAssets; }
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>WPF PieceByPieceHostService.cs:164-168 and SeasonRecapService.cs:182-197.
    /// One day event per fresh attempt, including failed setup; no recap Catalog entry or refocus credit.</summary>
    [Fact]
    public void PieceByPiece_FreshLaunchCreditsDayLog_RefocusDoesNot() => Run((shell, launcher) =>
    {
        var property = typeof(ConditioningControlPanel.Avalonia.App).GetProperty("FeatureDayLog",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        var folder = Directory.CreateTempSubdirectory("pbp-credit-").FullName;
        using var log = new FeatureDayLogService(() => null, Path.Combine(folder, "day.json"));
        try
        {
            property.SetValue(null, log);
            int Uses() => log.BuildWirePayload(10).Sum(d => d.TryGetValue("e_pb", out var n) ? Convert.ToInt32(n) : 0);
            var bucket = CoreSettings.Current.SeasonStatsSeason;
            var counts = CoreSettings.Current.SeasonFeatureUse.ToArray();
            launcher.Hide();
            MainShellWindow.LeashGateDueProvider = () => true;
            MainShellWindow.PresentLeashGateProvider = _ => { };
            launcher.Play(LauncherCards.Find(GameWindow.PbpId)!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Uses());
            MainShellWindow.LeashGateDueProvider = () => false;
            var first = GameWindow.Launch(GameWindow.PbpId)!;
            Assert.Equal(1, Uses());
            Assert.Same(first, GameWindow.Launch(GameWindow.PbpId));
            Assert.Equal(1, Uses());
            first.Close();
            Dispatcher.UIThread.RunJobs();
            var second = GameWindow.Launch(GameWindow.PbpId)!;
            Assert.NotSame(first, second);
            Assert.Equal(2, Uses());
            Assert.Equal(bucket, CoreSettings.Current.SeasonStatsSeason);
            Assert.Equal(counts, CoreSettings.Current.SeasonFeatureUse.ToArray());
            second.Close();
            Dispatcher.UIThread.RunJobs();
            var server = GameWindow.Server;
            try
            {
                GameWindow.Server = () => throw new InvalidOperationException("setup failed");
                var error = Assert.Throws<InvalidOperationException>(() => GameWindow.Launch(GameWindow.PbpId));
                Assert.Equal("setup failed", error.Message);
                Assert.Equal(3, Uses()); // WPF :168 runs before ChaosWebViewHost construction (:197).
                Assert.False(GameWindow.IsAnyOpen());
            }
            finally { GameWindow.Server = server; }
        }
        finally
        {
            property.SetValue(null, previous);
            log.Dispose();
            Directory.Delete(folder, true);
        }
    });

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
            Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:media-state", "owned", "pbp:look" }, sent.Select(o => (string?)o["type"]));
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

            w.Web.OnWebMessage(new JObject { ["type"] = "pbp:net", ["id"] = "escape",
                ["path"] = "/v2/pbp/../user/me" }.ToString());
            Assert.Equal("forbidden_path", (string?)sent.Last()["body"]);
            var beforeWatch = fake.Seen;
            w.Web.OnWebMessage(new JObject { ["type"] = "pbp:net", ["id"] = "watch",
                ["path"] = "/v2/pbp/watch/m_0123456789abcdef/chunk/0" }.ToString());
            Assert.True(w.PbpPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(beforeWatch, fake.Seen);
            Assert.False(fake.Seen!.Headers.Contains("X-Auth-Token"));
            Assert.Equal(200, (int)sent.Single(o => (string?)o["id"] == "watch")["status"]!);

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

    /// <summary>WPF PieceByPieceHostService.cs:165,230-232,284: opening, ready and a repeat
    /// launch all return keyboard focus. The headless WebHost fallback is focusable for this probe;
    /// the same FocusPage call targets NativeWebView when an engine is attached.</summary>
    [Theory]
    [InlineData("open")]
    [InlineData("ready")]
    [InlineData("reopen")]
    public void PieceByPiece_ReturnsKeyboardToPage(string path) => RunBoard(() =>
    {
        var board = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
        board.Web.Focusable = true;
        board.Load(GameWindow.Server());
        board.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            if (path != "open")
            {
                var other = new global::Avalonia.Controls.Button { Content = "Focus probe" };
                // A sibling can take focus without replacing the page or its native host.
                var panel = new global::Avalonia.Controls.StackPanel();
                board.Content = null;
                panel.Children.Add(board.Web);
                panel.Children.Add(other);
                board.Content = panel;
                Assert.True(other.Focus());
                Assert.False(board.Web.IsFocused);
                if (path == "ready") board.HandleMessage("{\"type\":\"ready\"}");
                else Assert.Same(board, GameWindow.Launch(GameWindow.PbpId));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(board.Web.IsFocused);
        }
        finally { board.Close(); }
    });

    [Theory]
    [InlineData("windows")]
    [InlineData("gtk")]
    [InlineData("wpe")]
    public void PieceByPiece_NativeEnvironment_IsolatesProfileAndConfiguresAutoplay(string engine) => Run((shell, launcher) =>
    {
        var type = engine switch
        {
            "windows" => typeof(WindowsWebView2EnvironmentRequestedEventArgs),
            "gtk" => typeof(GtkWebViewEnvironmentRequestedEventArgs),
            _ => typeof(LinuxWpeWebViewEnvironmentRequestedEventArgs),
        };
        var board = GameWindow.Launch(GameWindow.PbpId)!;
        var args = (WebViewEnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(type);
        // Drive the registered native callback, not the policy helper directly.
        board.Web.OnEnvironmentRequested(args);
        var folder = Path.Combine(CorePaths.UserData, "browser_data_piecebypiece_avalonia");
        Assert.True(Directory.Exists(folder));
        Assert.NotEqual(WebViewProfile.Folder, folder);
        switch (args)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                Assert.Equal(folder, windows.UserDataFolder);
                Assert.Equal("--autoplay-policy=no-user-gesture-required", windows.AdditionalBrowserArguments);
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                Assert.Equal(folder, gtk.BaseDataDirectory);
                Assert.Equal(Path.Combine(folder, "cache"), gtk.BaseCacheDirectory);
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                Assert.Equal(folder, wpe.DataDirectory);
                Assert.Equal(Path.Combine(folder, "cache"), wpe.CacheDirectory);
                break;
        }
        // The board policy must not leak to another game's environment.
        var other = new GameWindow(GameWindow.Games["breakoutdemo"]);
        var untouched = (WebViewEnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(type);
        other.Web.OnEnvironmentRequested(untouched);
        switch (untouched)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                Assert.Null(windows.UserDataFolder);
                Assert.Null(windows.AdditionalBrowserArguments);
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk: Assert.Null(gtk.BaseDataDirectory); break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe: Assert.Null(wpe.DataDirectory); break;
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PieceByPiece_FirstEscapePauses_SecondEscapeCloses(bool overrideAll) => Run((shell, launcher) =>
    {
        CoreSettings.Current.PanicKey = "Escape";
        CoreSettings.Current.PanicOverridesAll = overrideAll;
        launcher.Hide();
        var board = GameWindow.Launch("piecebypiece")!;
        board.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(board.IsActive);
        var sent = new List<JObject>();
        board.Posted += json => sent.Add(JObject.Parse(json));
        board.Web.OnNavigationCompleted(board.PageUrl!);
        board.Web.OnWebMessage("{\"type\":\"ready\"}");
        var now = DateTime.Now;
        shell.HandlePanicKeyPress(now);
        Assert.True(board.IsVisible);
        Assert.Single(sent, x => (string?)x["type"] == "pbp:escape");
        shell.HandlePanicKeyPress(now.AddSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Single(sent, x => (string?)x["type"] == "pbp:escape");
    });

    [Fact]
    public void PieceByPiece_EscapeDuringBoot_DoesNotQueueAPause() => Run((shell, launcher) =>
    {
        CoreSettings.Current.PanicKey = "Escape";
        launcher.Hide();
        var board = GameWindow.Launch("piecebypiece")!;
        board.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(board.IsActive);
        var sent = new List<JObject>();
        board.Posted += json => sent.Add(JObject.Parse(json));
        // Main 251b60807 (frontGameReady): a board still booting cannot pause, so its Escape is a full panic,
        // which closes it through PanicSurfaces "games". Nothing is queued for a page that never got ready.
        shell.HandlePanicKeyPress(DateTime.Now);
        Assert.Empty(sent);
        Assert.False(GameWindow.IsAnyOpen());
        Assert.DoesNotContain(sent, x => (string?)x["type"] == "pbp:escape");
    });

    [Theory]
    [InlineData("F9", true)]
    [InlineData("Escape", false)]
    public void PieceByPiece_ReboundOrBackgroundPanic_ClosesImmediately(string key, bool foreground) => Run((shell, launcher) =>
    {
        CoreSettings.Current.PanicKey = key;
        launcher.Hide();
        var board = GameWindow.Launch("piecebypiece")!;
        var sent = new List<JObject>();
        board.Posted += json => sent.Add(JObject.Parse(json));
        board.Web.OnNavigationCompleted(board.PageUrl!);
        board.Web.OnWebMessage("{\"type\":\"ready\"}");
        if (foreground) board.Activate();
        else { shell.Show(); shell.Activate(); }
        Dispatcher.UIThread.RunJobs();
        if (!foreground)
        {
            // Headless Activate does not deactivate the previous window; deliver the native
            // notification after queued activation work, as a compositor does on focus loss.
            var deactivated = typeof(global::Avalonia.Platform.IWindowBaseImpl).GetProperty("Deactivated",
                global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public
                | global::System.Reflection.BindingFlags.NonPublic)!;
            Assert.IsType<Action>(deactivated.GetValue(board.PlatformImpl))();
        }
        Assert.Equal(foreground, board.IsActive);
        shell.HandlePanicKeyPress(DateTime.Now);
        Assert.False(GameWindow.IsAnyOpen());
        Assert.DoesNotContain(sent, x => (string?)x["type"] == "pbp:escape");
    });

    [Theory]
    [InlineData(0, true, false, false, 0)]
    [InlineData(0, false, false, true, 0)]
    [InlineData(0, true, true, true, 0.5)]
    [InlineData(0, false, true, true, 0)]
    [InlineData(1, true, true, true, 0)]
    [InlineData(2, true, true, true, 0)]
    public void PieceByPiece_MotionCapsAndGentleIntro(int level, bool osAnimations, bool gentle, bool reduced, double speed)
        => RunBoard(() =>
    {
        var s = CoreSettings.Current;
        s.MotionLevel = (global::ConditioningControlPanel.Models.MotionLevel)level;
        s.GentleVisuals = gentle;
        s.GentleSpeed = 0.2;
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        w.PbpOsAnimations = () => Task.FromResult(osAnimations);
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:media-state", "owned", "pbp:look" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal(false, (bool?)sent[0]["prizeCounter"]);
        Assert.Equal(reduced, (bool)sent[0]["reducedMotion"]!);
        Assert.Equal(speed, (double)sent[0]["gentleIntroSpeed"]!);
    });

    [Fact]
    public void PieceByPiece_WatchPreferencesValidatePersistAndReachNextBoot() => RunBoard(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.PbpLetPeopleWatch, s.PbpWatchEffects, s.PbpWatchAmount);
        try
        {
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            void Setting(string key, JToken value) => w.HandleMessage(new JObject
                { ["type"] = "pbp:setting", ["key"] = key, ["value"] = value }.ToString());
            var panicEnabled = s.PanicKeyEnabled;
            Setting("letPeopleWatch", false);
            Setting("watchEffects", false);
            Setting("watchAmount", "more");
            Setting("watchEffects", "true");   // typed whitelist, no coercion
            Setting("watchAmount", "huge");
            Setting("PanicKeyEnabled", false);   // the page cannot change an arbitrary preference
            Assert.Equal(panicEnabled, s.PanicKeyEnabled);
            Assert.Equal((false, false, "more"), (s.PbpLetPeopleWatch, s.PbpWatchEffects, s.PbpWatchAmount));
            Assert.True(CoreSettings.Service!.HasPendingSave);   // asked to persist; Save() debounces 500 ms
            CoreSettings.Service.SaveImmediate();
            var persisted = new SettingsService().Current;
            Assert.Equal((false, false, "more"),
                (persisted.PbpLetPeopleWatch, persisted.PbpWatchEffects, persisted.PbpWatchAmount));
            w.Close();
            var next = GameWindow.Launch(GameWindow.PbpId)!;
            var sent = new List<JObject>();
            next.Posted += json => sent.Add(JObject.Parse(json));
            next.HandleMessage("{\"type\":\"ready\"}");
            var frame = sent.Single(o => (string?)o["type"] == "pbp:settings");
            Assert.False((bool)frame["letPeopleWatch"]!);
            Assert.False((bool)frame["watchEffects"]!);
            Assert.Equal("more", (string?)frame["watchAmount"]);
        }
        finally
        {
            (s.PbpLetPeopleWatch, s.PbpWatchEffects, s.PbpWatchAmount) = old;
            CoreSettings.Service?.Save();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PieceByPiece_SlowOsQueryKeepsBootOrder_AndClosedBoardPostsNothing(bool close) => RunBoard(() =>
    {
        var answer = new TaskCompletionSource<bool>();
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        Assert.True(w.PbpLocalProbe.Wait(TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();
        int queries = 0;
        w.PbpOsAnimations = () => { queries++; return answer.Task; };
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Empty(sent);
        Assert.Equal(1, queries);
        if (close) w.Close();
        answer.SetResult(false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.PbpPending.IsCompletedSuccessfully);
        if (close) Assert.Empty(sent);
        else Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:media-state", "owned", "pbp:look", "pbp:online-media" }, sent.Select(o => (string?)o["type"]));
    });

    // These tests exercise the real board and settings bridge, without the unrelated main shell FX.
    [Fact]
    public void PieceByPiece_LobbyIntentsFollowIdentityAndFreshSpectateUsesSettings() => RunBoard(() =>
    {
        Assert.Null(GameWindow.OpenChessLobby("join", "invalid"));
        Assert.Null(GameWindow.OpenChessLobby("spectate", "invalid"));
        var window = GameWindow.OpenChessLobby("host")!;
        window.PbpOsAnimations = () => Task.FromResult(true);
        var sent = new List<JObject>();
        window.Posted += json => sent.Add(JObject.Parse(json));
        Assert.Empty(sent);
        window.HandleMessage("{\"type\":\"ready\"}");
        // WPF OnPageReady: the lobby intent, then the wardrobe (owned + look).
        Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:friend", "owned", "pbp:look" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal("host", (string?)sent.Last(o => (string?)o["type"] == "pbp:friend")["mode"]);
        Assert.Same(window, GameWindow.OpenChessLobby("join", "p_table"));
        Assert.Equal("p_table", (string?)sent.Last()["target"]);
        GameWindow.OpenChessLobby("spectate", "m_0123456789abcdef");
        Assert.Equal("spectate", (string?)sent.Last()["mode"]);
        Assert.Equal("m_0123456789abcdef", (string?)sent.Last()["matchId"]);
        window.Close();
        window = GameWindow.OpenChessLobby("spectate", "m_fedcba9876543210")!;
        window.PbpOsAnimations = () => Task.FromResult(true);
        sent.Clear(); window.Posted += json => sent.Add(JObject.Parse(json));
        window.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("m_fedcba9876543210", (string?)sent.First()["spectateMatchId"]);
        Assert.Equal(new[] { "pbp:settings", "pbp:identity", "owned", "pbp:look" }, sent.Select(o => (string?)o["type"]));
        window.HandleMessage("{\"type\":\"ready\"}"); Assert.Equal(4, sent.Count);
        GameWindow.CloseAllForPanic();
        Assert.Empty(GameWindow.OpenWindowsForTests());
    });
    private static void RunBoard(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        var (oldSettings, oldServer) = (CoreSettings.ServiceProvider, GameWindow.Server);
        var root = Directory.CreateTempSubdirectory("pbp-settings-").FullName;
        using var server = new WebAssetServer(root);
        CoreSettings.ServiceProvider = () => service;
        GameWindow.Server = () => server;
        try { body(); }
        finally
        {
            GameWindow.CloseAllForPanic();
            (CoreSettings.ServiceProvider, GameWindow.Server) = (oldSettings, oldServer);
            Directory.Delete(root, true);
        }
    });

    [Fact]
    public void PieceByPiece_SettingsWhisperOwnClips_AndPostOnlyOnce() => RunBoard(() =>
    {
        var lib = Directory.CreateTempSubdirectory("pbp-whispers-").FullName;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        try
        {
            Directory.CreateDirectory(Path.Combine(lib, "braindrain"));
            File.WriteAllBytes(Path.Combine(lib, "braindrain", "a one.wav"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "braindrain", "ignored.txt"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "braindrain", "off.wav"), new byte[] { 1 });
            CoreSettings.Current.DisabledAssetPaths.Add("braindrain/off.wav");   // unchecked: never offered
            CorePaths.EffectiveAssetsProvider = () => lib;
            using var server = new WebAssetServer(lib) { AssetsRoot = () => lib };
            GameWindow.Server = () => server;
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            var settings = sent.Single(o => (string?)o["type"] == "pbp:settings");
            var url = (string)settings["whispers"]!.Single()!;
            Assert.Equal(w.PageUrl!.Authority, new Uri(url).Authority);
            Assert.EndsWith("/ccp.assets/braindrain/a%20one.wav", url);
            using var http = new System.Net.Http.HttpClient();   // the offered clip is actually served
            using var got = http.GetAsync(url + "?ccp_t=" + server.Token).GetAwaiter().GetResult();
            Assert.Equal(System.Net.HttpStatusCode.OK, got.StatusCode);
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Single(sent.Where(o => (string?)o["type"] == "pbp:settings"));
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = oldAssets;
            Directory.Delete(lib, true);
        }
    });

    [Fact]
    public void PieceByPiece_SettingsWhispers_AllOwnClipsUnchecked_FallBackToWords() => RunBoard(() =>
    {
        var dir = Directory.CreateTempSubdirectory("pbp-off-").FullName;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "braindrain"));
            File.WriteAllBytes(Path.Combine(dir, "braindrain", "off.wav"), new byte[] { 1 });
            var words = Directory.CreateDirectory(Path.Combine(dir, "words")).FullName;
            File.WriteAllBytes(Path.Combine(words, "drop.mp3"), new byte[] { 1 });
            CoreSettings.Current.DisabledAssetPaths.Add("braindrain/off.wav");
            CorePaths.EffectiveAssetsProvider = () => dir;
            using var server = new WebAssetServer(dir) { SubAudioRoot = () => null, WordsRoot = () => words };
            GameWindow.Server = () => server;
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.EndsWith("/ccp.words/drop.mp3", (string?)sent.Single(o => (string?)o["type"] == "pbp:settings")["whispers"]!.Single());
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = oldAssets;
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData(false, ConditioningControlPanel.Models.BuiltInMods.BambiSleepId, "ccp.words/drop.mp3")]
    [InlineData(true, ConditioningControlPanel.Models.BuiltInMods.BambiSleepId, "ccp.subaudio/BAMBI%20FREEZE.mp3")]
    [InlineData(true, ConditioningControlPanel.Models.BuiltInMods.CCPDefaultId, "ccp.words/drop.mp3")]
    public void PieceByPiece_SettingsWhisperFallback_RespectsSharedAudioPermission(bool audible, string modId, string expected)
        => RunBoard(() =>
    {
        var dir = Directory.CreateTempSubdirectory("pbp-fallback-").FullName;
        var (oldAssets, oldServer, oldMod) = (CorePaths.EffectiveAssetsProvider, GameWindow.Server, CoreMods.ActiveModIdProvider);
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
            var words = Directory.CreateDirectory(Path.Combine(dir, "words")).FullName;
            File.WriteAllBytes(Path.Combine(sub, "BAMBI FREEZE.mp3"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(words, "drop.mp3"), new byte[] { 1 });
            CorePaths.EffectiveAssetsProvider = () => dir;
            CoreMods.ActiveModIdProvider = () => modId;
            CoreSettings.Current.SubAudioEnabled = true;
            CoreSettings.Current.SubAudioMuted = !audible;
            using var server = new WebAssetServer(dir) { SubAudioRoot = () => sub, WordsRoot = () => words };
            GameWindow.Server = () => server;
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.EndsWith("/" + expected, (string?)sent.Single(o => (string?)o["type"] == "pbp:settings")["whispers"]!.Single());
        }
        finally
        {
            (CorePaths.EffectiveAssetsProvider, GameWindow.Server, CoreMods.ActiveModIdProvider) = (oldAssets, oldServer, oldMod);
            Directory.Delete(dir, true);
        }
    });

    // ---- Racing Thoughts (WPF Services/Chaos/CaucusHostService.cs) ----

    /// <summary>Signed in, owning <paramref name="owned"/> (null = nothing); XP and Sparks go to lists.</summary>
    private static void RunRace(string? owned, Action<LauncherWindow, List<double>, List<double>> body) => Run((_, launcher) =>
    {
        var (oldOwns, oldBank, oldXp, oldAssets, oldClock) = (RacingAccess.IsGrantedProvider, GameWindow.RaceBankSparks, CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider, GameWindow.RaceUtcNow);
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
            (RacingAccess.IsGrantedProvider, GameWindow.RaceBankSparks, CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider, GameWindow.RaceUtcNow) = (oldOwns, oldBank, oldXp, oldAssets, oldClock);
            Directory.Delete(lib, true);
        }
    });

    // Retained 2D bridge regressions run directly, never through a production door.
    private static GameWindow OpenRetiredRace()
    {
        var w = new GameWindow(new GameWindow.Game("race", "launcher_game_race_title", "dtrh/race.html"));
        w.Load(GameWindow.Server());
        w.Show();
        return w;
    }

    /// <summary>WPF OnPageReady :255 (init with the race settings block, then manifest) and OnRunEnded :431:
    /// one payout per run, score / 5 under 250 XP a minute, Sparks banked on score / 5.</summary>
    [Fact]
    public void Race_BootAndOnePayoutPerRun() => RunRace("rt.original.03", (_, xp, sparks) =>
    {
        var w = OpenRetiredRace();
        Assert.Contains("dtrh/race.html", w.PageUrl!.AbsolutePath);
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(new[] { "init", "manifest", "loom-list" }, sent.Select(o => (string?)o["type"]));
        // WPF 7.2.3: the door is open, so init hands every track, not just the one owned.
        Assert.Equal(Enumerable.Range(0, ConditioningControlPanel.Services.Prizes.PrizeGrants.RacingTrackMax + 1), sent[0]["settings"]!["racingTracks"]!.Values<int>());
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

    /// <summary>WPF 7.2.3 (owner 2026-11-05): the purchase door is open, Classic is free with every track.
    /// The page gets every track whatever the store says, so a grant change neither restages the menu nor
    /// closes the host; the 3D tile stays (Prime padlock). The armed-door branch stays in RacingAccess.</summary>
    [Fact]
    public void Race_with_the_door_open_ignores_grant_changes() => RunRace("rt.original.03", (launcher, _, _) =>
    {
        var tracks = new HashSet<string> { "rt.original.03" };
        RacingAccess.IsGrantedProvider = tracks.Contains;
        launcher.BuildTiles();
        var w = OpenRetiredRace();
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        tracks.Add("rt.original.04");
        ConditioningControlPanel.Services.Prizes.PrizeGrants.RaiseGrantsChanged();
        tracks.Clear();
        ConditioningControlPanel.Services.Prizes.PrizeGrants.RaiseGrantsChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(sent, m => (string?)m["type"] == "race-ownership");
        Assert.True(GameWindow.IsAnyOpen());
        Assert.Equal(ConditioningControlPanel.Services.Prizes.PrizeGrants.RacingTrackMax + 1, RacingAccess.OwnedTracks.Length);
        Assert.Empty(RacingAccess.PurchasedTracks);
        Assert.Contains(launcher.FindControl<global::Avalonia.Controls.Primitives.UniformGrid>("GamesGrid")!.Children,
            c => Equals(c.Tag, "race"));
        w.Close();
        Dispatcher.UIThread.RunJobs();
    });

    /// <summary>WPF StartHeartbeatWatch :595: idle until ready; silent past 20 s on the menu (10 s mid-run)
    /// gets one ping, a pong resets it, still silent on the next tick closes the window.</summary>
    [Fact]
    public void Race_HeartbeatWatchdog_PingsThenCloses() => RunRace("rt.original.00", (_, _, _) =>
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = t0;
        GameWindow.RaceUtcNow = () => now;                // stepped clock (P08); RunRace restores it
        var w = OpenRetiredRace();
        Assert.True(w.RaceWatchRunning);                // WPF Launch arms the watch
        Assert.Equal("idle", w.CheckRaceHeartbeat(t0.AddSeconds(60)));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("ok", w.CheckRaceHeartbeat(t0.AddSeconds(15)));
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.Equal("pinged", w.CheckRaceHeartbeat(t0.AddSeconds(15)));
        now = t0.AddSeconds(20);
        w.HandleMessage("{\"type\":\"pong\"}");
        Assert.Equal("ok", w.CheckRaceHeartbeat(t0.AddSeconds(25)));
        Assert.Equal("pinged", w.CheckRaceHeartbeat(t0.AddSeconds(31)));
        Assert.Equal("closed", w.CheckRaceHeartbeat(t0.AddSeconds(36)));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.RaceWatchRunning);               // DisposeAll stops it (P65)
    });

    // ---- Racing Thoughts own tracks (WPF CaucusHostService.cs :353-368, :736-1035) ----

    // Restack b76 (#3286). WPF CaucusHostService.cs:383,435: both paths stop the file clock.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Race_RunEndAndExit_StopTheAudio(bool exit) => RunRace("rt.original.03", (_, _, _) =>
        WithTrackFiles((_, _) => Tone(1), paths =>
    {
        var player = new FakeTrackPlayer();
        try
        {
            var w = OpenRetiredRace();
            w.RacePickFile = () => Task.FromResult<string?>(paths[0]);
            w.RaceNewPlayer = () => player;
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            w.HandleMessage("{\"type\":\"track-play\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage(exit ? "{\"type\":\"exit\"}" : "{\"type\":\"run-ended\"}");
            Assert.False(player.IsPlaying);
            Assert.False(w.RaceTrackClockRunning);
        }
        finally { GameWindow.CloseAllForPanic(); }
    }));

    private sealed class FakeTrackPlayer : IRaceTrackPlayer
    {
        public readonly List<string> Calls = new();
        public string? Loaded;
        public bool Disposed;
#pragma warning disable CS0067
        public event Action? Ended;
#pragma warning restore CS0067
        public double PositionSec => 1.5;
        public double DurationSec => 30;
        public bool IsPlaying { get; private set; }
        public void Load(string path) { Loaded = path; Calls.Add("Load"); }
        public void RefreshVolume() => Calls.Add("RefreshVolume");
        public void Play() { IsPlaying = true; Calls.Add("Play"); }
        public void Pause() { IsPlaying = false; Calls.Add("Pause"); }
        public void Resume() { IsPlaying = true; Calls.Add("Resume"); }
        public void Stop() { IsPlaying = false; Calls.Add("Stop"); }
        public void Dispose() => Disposed = true;
    }

    /// <summary>A unique audio-ish file (its hash keys the chart cache) and a decoder seed; both undone after.</summary>
    private static void WithTrackFiles(Func<string, CancellationToken, float[]>? decode, Action<string[]> body)
    {
        var old = TrackDecoder.DecodeProvider;
        var dir = Directory.CreateTempSubdirectory("race-track-").FullName;
        var files = new[] { Path.Combine(dir, "one.mp3"), Path.Combine(dir, "two.mp3") };
        foreach (var f in files) File.WriteAllBytes(f, Guid.NewGuid().ToByteArray());
        try
        {
            TrackDecoder.DecodeProvider = decode == null ? null : (path, _, ct) => decode(path, ct);
            body(files);
        }
        finally
        {
            TrackDecoder.DecodeProvider = old;
            foreach (var f in files) { var chart = TrackChartCache.PathFor(TrackDecoder.HashFile(f)); if (File.Exists(chart)) File.Delete(chart); }   // no decoder writes no chart, and maybe no charts dir
            Directory.Delete(dir, true);
        }
    }

    private static float[] Tone(int seconds) =>
        Enumerable.Range(0, TrackPcm.SampleRate * seconds).Select(i => (float)Math.Sin(i * 0.05) * (i / 8000 % 2)).ToArray();

    private static void Settle(GameWindow w)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.RaceAnalysis.Wait(TimeSpan.FromSeconds(20)));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>WPF PickTrack -> BeginTrack -> AnalyzeTrack (:781-940): progress, a partial chart then the full one,
    /// cached so a second pick of the same file is one chart and no decode; play/pause drive the player and post
    /// the clock (:957-1035); panic tears it down (DisposeAll :686: StopTrack, player disposed, clock stopped).</summary>
    [Fact]
    public void Race_TrackPick_ChartsPlaysAndPanicStops() => RunRace("rt.original.03", (_, _, _) =>
    {
        int decodes = 0;
        WithTrackFiles((_, _) => { decodes++; return Tone(4); }, files =>
        {
            var player = new FakeTrackPlayer();
            var w = OpenRetiredRace();
            using (var real = w.RaceNewPlayer!()) Assert.IsType<RaceTrackPlayer>(real);   // this head's own player
            w.RaceNewPlayer = () => player;
            w.RacePickFile = () => Task.FromResult<string?>(files[0]);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            Assert.Equal(files[0], player.Loaded);
            Assert.True(w.RaceTrackClockRunning);
            Assert.DoesNotContain(sent, o => (string?)o["type"] == "track-error");
            var first = sent.First();
            Assert.Equal(("track-progress", "decode", "one.mp3"), ((string?)first["type"], (string?)first["stage"], (string?)first["name"]));
            Assert.Equal(new[] { true, false }, sent.Where(o => (string?)o["type"] == "track-chart").Select(o => (bool)o["partial"]!));
            Assert.Equal(1, decodes);

            sent.Clear();
            w.HandleMessage("{\"type\":\"track-pick\"}");   // same file again: the cache door
            Settle(w);
            Assert.Equal(new[] { false }, sent.Where(o => (string?)o["type"] == "track-chart").Select(o => (bool)o["partial"]!));
            Assert.Equal(1, decodes);

            sent.Clear();
            player.Calls.Clear();
            w.HandleMessage("{\"type\":\"track-play\"}");
            var clock = sent.Last(o => (string?)o["type"] == "track-clock");
            Assert.Equal((1.5, true, 30.0), ((double)clock["t"]!, (bool)clock["playing"]!, (double)clock["durationSec"]!));
            w.HandleMessage("{\"type\":\"track-pause\",\"on\":true}");
            Assert.False((bool)sent.Last(o => (string?)o["type"] == "track-clock")["playing"]!);
            w.HandleMessage("{\"type\":\"track-pause\",\"on\":false}");
            Assert.Equal(new[] { "RefreshVolume", "Play", "Pause", "Resume" }, player.Calls);

            PanicSurfaces.All.Single(x => x.Id == "games").Stop(MainShellWindow.Current!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.True(player.Disposed);
            Assert.Equal("Stop", player.Calls.Last());
            Assert.False(w.RaceTrackClockRunning);
        });
    });

    /// <summary>A cancelled dialog and a track-cancel with nothing running answer progress "cancelled" (:796, :947);
    /// a build with no player and no decoder answers track-error, never a crash (:836, :929).</summary>
    [Fact]
    public void Race_TrackPick_CancelAndNoAudioAreFramesNotCrashes() => RunRace("rt.original.03", (_, _, _) =>
    {
        WithTrackFiles(null, files =>
        {
            var w = OpenRetiredRace();
            w.RaceNewPlayer = null;   // a build with no player
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.RacePickFile = () => Task.FromResult<string?>(null);
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            w.HandleMessage("{\"type\":\"track-cancel\"}");
            Assert.Equal(new[] { "cancelled", "cancelled" }, sent.Select(o => (string?)o["stage"]));

            sent.Clear();
            w.RacePickFile = () => Task.FromResult<string?>(files[0]);
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            var errors = sent.Where(o => (string?)o["type"] == "track-error").Select(o => (string?)o["message"]).ToList();
            Assert.Equal(new[] { GameWindow.RaceNoAudioMessage, "this build cannot decode audio" }, errors);
            Assert.False(w.RaceTrackClockRunning);
            w.HandleMessage("{\"type\":\"track-play\"}");   // nothing loaded: no clock, no throw
            Assert.DoesNotContain(sent, o => (string?)o["type"] == "track-clock");
            w.Close();
        });
    });

    /// <summary>WPF BeginTrack/AnalyzeTrack generations (:812, :922): a newer pick cancels the one in flight, and
    /// the superseded worker says nothing (no "cancelled", no chart); only the new pick's charts reach the page.</summary>
    [Fact]
    public void Race_TrackPick_NewerPickSilencesTheOldWorker() => RunRace("rt.original.03", (_, _, _) =>
    {
        using var entered = new ManualResetEventSlim(false);
        WithTrackFiles((path, ct) =>
        {
            if (path.EndsWith("one.mp3")) { entered.Set(); ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)); ct.ThrowIfCancellationRequested(); }
            return Tone(3);
        }, files =>
        {
            var w = OpenRetiredRace();
            w.RaceNewPlayer = () => new FakeTrackPlayer();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.RacePickFile = () => Task.FromResult<string?>(files[0]);
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Dispatcher.UIThread.RunJobs();
            var stale = w.RaceAnalysis;
            Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));   // the first worker is really inside its decode
            w.RacePickFile = () => Task.FromResult<string?>(files[1]);
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            Assert.True(stale.Wait(TimeSpan.FromSeconds(20)));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(sent, o => (string?)o["stage"] == "cancelled" || (string?)o["type"] == "track-error");
            var charts = sent.Where(o => (string?)o["type"] == "track-chart").ToList();
            Assert.Equal(2, charts.Count);
            Assert.All(charts, c => Assert.Equal("two.mp3", (string?)c["chart"]!["source"]!["name"]));
            w.Close();
        });
    });

    // ---- Down the Rabbit Hole (WPF Services/Chaos/DtrhHostService.cs, slice A) ----

    /// <summary>Lab (or not), signed in, a throwaway library; XP and each payout's counts-as-run go to lists.</summary>
    private static void RunDtrh(bool lab, Action<MainShellWindow, List<double>, List<bool>, List<string>> body) => Run((shell, launcher) =>
    {
        var (oldLab, oldPremium, oldFree, oldDenied, oldBank, oldXp, oldAssets, oldClock) = (CoreEntitlement.HasLabProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
            CoreEntitlement.ShowDeniedHandler, GameWindow.DtrhBankSparks, CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider, GameWindow.DtrhUtcNow);
        var (oldRuns, oldForce) = (ChaosMeta.State.RunsCompleted, ChaosMeta.State.ForceScriptedRun);
        var (oldFavorites, oldMerge) = (GameWindow.DtrhFavorites, GameWindow.DtrhMergeAssetStats);
        var oldRelaunched = GameWindow.DtrhRelaunchedOnce;
        GameWindow.DtrhRelaunchedOnce = false;
        var lib = Directory.CreateTempSubdirectory("dtrh-lib-").FullName;
        var xp = new List<double>();
        var runs = new List<bool>();
        var denied = new List<string>();
        try
        {
            // The engagement store stays out of lifecycle tests: no profile write, no ambient history.
            GameWindow.DtrhFavorites = _ => new List<string>();
            GameWindow.DtrhMergeAssetStats = _ => { };
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => lab;
            CoreEntitlement.HasPremiumProvider = () => lab;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreEntitlement.ShowDeniedHandler = v => denied.Add(v.Feature);
            GameWindow.DtrhBankSparks = (_, countsAsRun) => { runs.Add(countsAsRun); return 5; };
            CoreProgression.AddXPProvider = (amount, _) => xp.Add(amount);
            CorePaths.EffectiveAssetsProvider = () => lib;
            ChaosMeta.State.ForceScriptedRun = false;    // a forced replay would write the meta save
            Directory.CreateDirectory(Path.Combine(lib, "images"));
            Directory.CreateDirectory(Path.Combine(lib, "videos"));
            File.WriteAllBytes(Path.Combine(lib, "images", "pic.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "videos", "clip.mp4"), new byte[] { 1 });
            launcher.Hide();
            body(shell, xp, runs, denied);
        }
        finally
        {
            (CoreEntitlement.HasLabProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler, GameWindow.DtrhBankSparks,
                CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider, GameWindow.DtrhUtcNow) = (oldLab, oldPremium, oldFree, oldDenied, oldBank, oldXp, oldAssets, oldClock);
            (ChaosMeta.State.RunsCompleted, ChaosMeta.State.ForceScriptedRun) = (oldRuns, oldForce);
            GameWindow.CloseAllForPanic();
            (GameWindow.DtrhFavorites, GameWindow.DtrhMergeAssetStats) = (oldFavorites, oldMerge);
            GameWindow.DtrhRelaunchedOnce = oldRelaunched;
            Directory.Delete(lib, true);
        }
    });

    /// <summary>WPF DtrhHostService.cs:925-954: an actual page boot failure opens the
    /// classic first run or the Warren. The Warren's FALL IN then opens the bound HUD.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Dtrh_BootError_reaches_the_classic_Hud(int completed) => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var oldIntro = ChaosMeta.State.SeenIntroGuide;
        ChaosMeta.State.RunsCompleted = completed;
        ChaosMeta.State.SeenIntroGuide = true;
        ChaosMeta.Save();   // the fallback routes from the loaded save, as WPF App.xaml.cs:2827 does
        ConditioningControlPanel.Avalonia.Views.Chaos.ChaosHubWindow? hub = null;
        void Opened(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is ConditioningControlPanel.Avalonia.Views.Chaos.ChaosHubWindow h) hub = h;
        }
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>(Opened);
        try
        {
            var web = GameWindow.Launch("dtrh")!;
            Assert.True(web.IsVisible);
            web.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"renderer failed\"}");
            Assert.False(web.IsVisible);
            Dispatcher.UIThread.RunJobs();   // the classic door is queued behind the teardown (WPF BeginInvoke)
            if (completed == 1)
            {
                Assert.NotNull(hub);
                Assert.False(ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.IsActive);
                hub.FallIn();
                Dispatcher.UIThread.RunJobs();
            }
            var host = ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.Hud;
            Assert.NotNull(host);
            Assert.True(host.IsVisible);
            Assert.Same(ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.State, host.DataContext);
            Assert.Equal(completed == 0,
                ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.State!.Config.ScriptedFirstRun);
            var state = host.DataContext;
            web.HandleMessage("{\"type\":\"boot-error\"}");
            Assert.Same(state, ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.State);
        }
        finally
        {
            ChaosMeta.State.SeenIntroGuide = oldIntro;
            hub?.Close();
            ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.ForceShutdown();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dtrh_PayoutUsesSkillTreeMultiplier(bool pinkRush) => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var settings = CoreSettings.Current;
        var (oldLevel, oldXp, oldRush) = (settings.PlayerLevel, settings.PlayerXP, settings.PinkRushActive);
        settings.PlayerLevel = 100;
        settings.PlayerXP = 0;
        settings.UnlockedSkills.Add("sparkle_boost_1");
        settings.UnlockedSkills.Add("pink_rush");
        settings.PinkRushActive = pinkRush;
        CoreProgression.AddXPProvider = ProgressionBank.Add;
        var awards = new List<ProgressionBank.XpAward>();
        var questXp = new List<double>();
        void Awarded(ProgressionBank.XpAward award) => awards.Add(award);
        void QuestAwarded(double amount, string source) => questXp.Add(amount);
        ProgressionBank.XPAwarded += Awarded;
        ProgressionBank.QuestAwarded += QuestAwarded;
        try
        {
            var w = GameWindow.Launch("dtrh")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            var payout = Assert.Single(sent);
            var expected = global::ConditioningControlPanel.Models.SkillTreeRules.GetTotalXpMultiplier(settings, DateTime.Now.Hour, 0.0);
            Assert.Equal(expected, (double)payout["skillMult"]!, 6);
            Assert.Equal(300 * expected, (double)payout["finalXp"]!, 6);
            var credited = 300 * expected * ConditioningControlPanel.Services.Descent.DescentCycleXp.XpBonusFor(settings);
            Assert.Equal(credited, Assert.Single(awards).Amount, 6);
            Assert.Equal(credited, settings.PlayerXP, 6);
            Assert.Equal(300, Assert.Single(questXp), 6);
            w.Close();
        }
        finally
        {
            ProgressionBank.XPAwarded -= Awarded;
            ProgressionBank.QuestAwarded -= QuestAwarded;
            (settings.PlayerLevel, settings.PlayerXP, settings.PinkRushActive) = (oldLevel, oldXp, oldRush);
            settings.UnlockedSkills.Remove("pink_rush");
            settings.UnlockedSkills.Remove("sparkle_boost_1");
        }
    });

    /// <summary>WPF LauncherCatalogue (Basic door, DemandPremium refusal), OnPageReady :271 (init with motionLevel
    /// and runSetup, then the manifest with videos), OnRequestRun :523, OnRunEnded :583 (one payout per descent)
    /// and DisposeAll :1002 (a window closed mid-descent banks its last snapshot, paid by the host's clock).</summary>
    [Fact]
    public void Dtrh_FullscreenSetRestoresFrameAndReplies() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        w.Position = new PixelPoint(120, 80);
        w.Width = 800;
        w.Height = 600;
        Dispatcher.UIThread.RunJobs();
        var position = w.Position;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.Equal(WindowState.FullScreen, w.WindowState);
        Assert.True((bool)Assert.Single(sent)["on"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Assert.Equal(WindowState.Normal, w.WindowState);
        Assert.Equal(position, w.Position);
        Assert.Equal(800, w.Width);
        Assert.Equal(600, w.Height);
        Assert.False((bool)Assert.Single(sent)["on"]!);
    });

    private static global::Avalonia.Platform.Screen DtrhScreen(PixelRect bounds, double scale, bool primary)
    {
        var screen = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(bounds.X + 1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, scale);
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, primary);
        return screen;
    }

    [Fact]
    public void Dtrh_FullscreenFromMaximizedPreservesNormalFrame() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        w.Position = new PixelPoint(120, 80);
        w.Width = 800;
        w.Height = 600;
        Dispatcher.UIThread.RunJobs();
        w.WindowState = WindowState.Maximized;
        w.Position = new PixelPoint(0, 0); // model the native maximized position
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Assert.Equal(WindowState.Maximized, w.WindowState);
        w.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new PixelPoint(120, 80), w.Position);
        Assert.Equal(800, w.Width);
        Assert.Equal(600, w.Height);
    });

    [Fact]
    public void Dtrh_RemovedMonitorCentersFrameOnLiveMonitor() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var previous = ScreenList.QueryOverride;
        var primary = DtrhScreen(new PixelRect(0, 0, 1600, 900), 1, true);
        var secondary = DtrhScreen(new PixelRect(1600, 0, 1600, 900), 1, false);
        var screens = new[] { primary, secondary };
        ScreenList.QueryOverride = _ => screens;
        try
        {
            var w = GameWindow.Launch("dtrh")!;
            w.Position = new PixelPoint(2000, 100);
            w.Width = 800;
            w.Height = 600;
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
            screens = new[] { primary };
            w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.Equal(new PixelPoint(400, 150), w.Position);
            Assert.Equal(800, w.Width);
            Assert.Equal(600, w.Height);
        }
        finally { ScreenList.QueryOverride = previous; }
    });

    [Fact]
    public void Dtrh_ModSwitchRequestsEndWithoutClosingOtherGames() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        using var snapshot = new CoreModsSnapshot();
        var oldSettings = CoreSettings.ServiceProvider;
        try
        {
            global::ConditioningControlPanel.Avalonia.App.StartMods();
            var w = GameWindow.Launch("dtrh")!;
            var other = GameWindow.Launch("breakoutdemo")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            sent.Clear();
            var target = global::ConditioningControlPanel.Avalonia.App.Mods!.ActiveModId == BuiltInMods.SissyHypnoId
                ? BuiltInMods.BambiSleepId : BuiltInMods.SissyHypnoId;
            global::ConditioningControlPanel.Avalonia.App.Mods.ActivateMod(target);
            Assert.Contains(sent, x => (string?)x["type"] == "end-run");
            Assert.True(other.IsVisible);
            w.HandleMessage("{\"type\":\"exit-done\"}");
            Assert.False(w.IsVisible);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.App.ResetReleaseContent();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public void Dtrh_BasicDoor_BootRunLifecycle_AndCloseBanks()
    {
        RunDtrh(lab: false, (_, _, _, denied) =>
        {
            Assert.True(GameWindow.DtrhLocked());
            Assert.Null(GameWindow.Launch("dtrh"));
            Assert.Single(denied);
            Assert.False(GameWindow.IsAnyOpen());
        });
        RunDtrh(lab: true, (_, xp, runs, _) =>
        {
            var s = CoreSettings.Current;
            s.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Reduced;
            s.ChaosDifficulty = "Normal";
            Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "dtrh");
            Assert.False(GameWindow.DtrhLocked());
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = t0;
            GameWindow.DtrhUtcNow = () => now;
            var w = GameWindow.Launch("dtrh")!;
            Assert.Contains("dtrh/index.html", w.PageUrl!.AbsolutePath);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal(new[] { "init", "manifest", "loom-list" }, sent.Select(o => (string?)o["type"]));
            Assert.Equal("reduced", (string?)sent[0]["motionLevel"]);
            Assert.Equal("Normal", (string?)sent[0]["runSetup"]!["difficulty"]);
            Assert.Equal("Basic", (string?)sent[0]["plans"]!["basic"]!["name"]);
            Assert.EndsWith("/ccp.assets/images/pic.png", (string?)sent[1]["images"]!.Single()["url"]);
            Assert.EndsWith("/ccp.assets/videos/clip.mp4", (string?)sent[1]["videos"]!.Single()["url"]);
            sent.Clear();

            ChaosMeta.State.RunsCompleted = 0;
            w.HandleMessage("{\"type\":\"request-run\",\"setup\":{\"difficulty\":\"Hard\"}}");
            Assert.Equal("Hard", s.ChaosDifficulty);                                   // the hub's choice persists
            Assert.True((bool)Assert.Single(sent)["runConfig"]!["scriptedFirstRun"]!);  // the first descent is the classroom
            sent.Clear();

            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");   // no run-started: nothing
            Assert.Empty(sent);
            w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Easy\"}");
            Assert.True(w.DtrhRunActive);
            Assert.True(GameWindow.IsDtrhRunning);
            var opener = global::ConditioningControlPanel.Avalonia.Controls.ScrolllerCreditLine.Opener;
            global::ConditioningControlPanel.Avalonia.Controls.ScrolllerCreditLine.Opener = _ => true;
            s.StrictLockEnabled = false;
            try { Assert.False(global::ConditioningControlPanel.Avalonia.Controls.ScrolllerCreditLine.OpenHome()); }
            finally { global::ConditioningControlPanel.Avalonia.Controls.ScrolllerCreditLine.Opener = opener; }
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");   // a second end pays nothing
            Assert.False(GameWindow.IsDtrhRunning);
            var pay = Assert.Single(sent);
            Assert.Equal("payout-result", (string?)pay["type"]);
            Assert.Equal(300, (double)pay["baseXp"]!);
            Assert.Equal(5, (int)pay["sparksEarned"]!);
            Assert.Equal(new[] { 300.0 }, xp);
            Assert.Equal(new[] { true }, runs);

            // Closed with an endless descent still falling: the last snapshot is banked as left, paid for
            // the 90 s the host saw (+5 s slack), not the 600 s the page claims.
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-progress\",\"score\":100000,\"durationSec\":720,\"elapsedSec\":600}");
            now = t0.AddSeconds(90);
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.Equal(2, xp.Count);
            Assert.Equal(250.0 * 95 / 60, xp[1], 6);
            Assert.Equal(new[] { true, true }, runs);
        });
    }

    /// <summary>WPF MainWindow.xaml.cs:1731-1766: a live descent takes priority over either game handoff.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Chaos_ActiveDescentPanicOutranksOpenGames(bool withDtrh, bool countdown) => RunDtrh(lab: true, (shell, _, _, _) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled, oldOverride) = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = ("F8", true, false);
            var breakout = GameWindow.Launch("breakoutdemo")!;
            var frames = new List<JObject>();
            breakout.Posted += json => frames.Add(JObject.Parse(json));
            var dtrh = withDtrh ? GameWindow.Launch("dtrh")! : null;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            var overlay = ChaosRunHost.Overlay!;
            if (!countdown) overlay.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            Assert.True(ChaosRunHost.IsActive);
            Assert.Equal(!countdown, ChaosRunHost.IsDescending);

            shell.HandlePanicKeyPress(DateTime.Now);
            Assert.False(ChaosRunHost.IsActive);
            overlay.FinishCountdown(); // a queued start must not revive the run after panic
            Assert.False(ChaosRunHost.IsDescending);
            Assert.False(breakout.IsVisible);
            Assert.DoesNotContain(frames, f => (string?)f["type"] == "suspend");
            if (dtrh != null) Assert.False(dtrh.IsVisible);
            Assert.Equal(0, shell.PanicPressCountForTest);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = (oldKey, oldEnabled, oldOverride);
        }
    });

    /// <summary>WPF MainWindow.xaml.cs:1742-1769: DtRH takes an unclaimed ladder press before Breakout.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dtrh_PanicTakesPriorityOverOpenBreakout(bool ready) => RunDtrh(lab: true, (shell, _, _, _) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled, oldOverride) = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = ("F8", true, false);
            var breakout = GameWindow.Launch("breakoutdemo")!;
            var breakoutFrames = new List<JObject>();
            breakout.Posted += json => breakoutFrames.Add(JObject.Parse(json));
            var dtrh = GameWindow.Launch("dtrh")!;
            var dtrhFrames = new List<JObject>();
            dtrh.Posted += json => dtrhFrames.Add(JObject.Parse(json));
            if (ready) dtrh.HandleMessage("{\"type\":\"ready\"}");
            dtrhFrames.Clear();
            var now = DateTime.Now;

            shell.HandlePanicKeyPress(now);
            Assert.True(breakout.IsVisible);
            Assert.DoesNotContain(breakoutFrames, f => (string?)f["type"] == "suspend");
            Assert.Equal(0, shell.PanicPressCountForTest);
            if (ready)
            {
                var end = Assert.Single(dtrhFrames);
                Assert.Equal("end-run", (string?)end["type"]);
                Assert.Equal("host", (string?)end["reason"]);
                dtrh.HandleMessage("{\"type\":\"exit-done\"}");
            }
            Assert.False(dtrh.IsVisible);

            shell.HandlePanicKeyPress(now.AddSeconds(1));
            var suspend = Assert.Single(breakoutFrames, f => (string?)f["type"] == "suspend");
            Assert.True((bool)suspend["on"]!);
            Assert.True(breakout.IsVisible);
            Assert.Equal(0, shell.PanicPressCountForTest);
        }
        finally { (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = (oldKey, oldEnabled, oldOverride); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dtrh_PanicHandoffStopsCamera(bool ready) => RunDtrh(lab: true, (shell, _, _, _) =>
    {
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll, s.WebcamConsentGiven,
            s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = ("F8", true, false);
            s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlinkTrainerSessionTests.BlackFrames();
            var w = GameWindow.Launch("dtrh")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            if (ready) w.HandleMessage("{\"type\":\"ready\"}");
            sent.Clear();
            Assert.True(tracker.Start(), tracker.LastError);
            shell.HandlePanicKeyPress(DateTime.Now);
            Assert.True(SpinWait.SpinUntil(() => !tracker.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.True(WebcamConsent.IsCurrent(s));
            Assert.Equal(0, shell.PanicPressCountForTest);
            if (ready)
            {
                Assert.Equal("end-run", (string?)Assert.Single(sent)["type"]);
                w.HandleMessage("{\"type\":\"exit-done\"}");
            }
            Assert.False(w.IsVisible);
        }
        finally
        {
            tracker.Stop();
            SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(20));
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll, s.WebcamConsentGiven,
                s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void Dtrh_PanicRequestsEndAndPageSettlesOnlyOnce() => RunDtrh(lab: true, (shell, xp, runs, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"run-started\"}");
        w.HandleMessage("{\"type\":\"run-progress\",\"score\":100,\"durationSec\":180}");
        sent.Clear();
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        var end = Assert.Single(sent);
        Assert.Equal("end-run", (string?)end["type"]);
        Assert.Equal("host", (string?)end["reason"]);
        Assert.True(w.IsVisible);
        w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
        w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
        w.HandleMessage("{\"type\":\"exit-done\"}");
        Assert.False(w.IsVisible);
        Assert.Single(xp);
        Assert.Single(runs);
    });

    /// <summary>WPF LauncherCatalogue.cs:243-248: the Play door asks DtRH's gate before Launch, so lapsed access with
    /// a descent still open is refused instead of refocusing it.</summary>
    [Fact]
    public void Dtrh_PlayDoor_LapsedAccessRefusesBeforeFocusingTheLiveHost() => RunDtrh(lab: true, (shell, _, _, denied) =>
    {
        var live = GameWindow.Launch("dtrh")!;
        var oldLaunch = global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView.LaunchGame;
        var launches = new List<string>();
        try
        {
            global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView.LaunchGame = launches.Add;
            // DtRH is a Basic door now (DtrhGate demands premium), so a lapse drops Basic too.
            CoreEntitlement.HasLabProvider = () => false;   // RunDtrh restores it
            CoreEntitlement.HasPremiumProvider = () => false;   // RunDtrh restores it
            shell.ShowTab("play");
            Dispatcher.UIThread.RunJobs();
            shell.Named<global::Avalonia.Controls.Control>("PlayTab")!
                .FindControl<global::Avalonia.Controls.Button>("BtnPlayDtrh")!
                .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(launches);
            Assert.Single(denied);
            Assert.Same(live, Assert.Single(GameWindow.OpenWindowsForTests()));
        }
        finally { global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView.LaunchGame = oldLaunch; }
    });

    [Fact]
    public void Dtrh_BasicAndDailyGiftAgreeAcrossDoorsAndPayout() => RunDtrh(lab: false, (shell, _, _, denied) =>
    {
        bool premium = false;
        string? freeKey = null;
        MainShellWindow.LeashGateDueProvider = () => false;
        CoreEntitlement.HasPremiumProvider = () => premium;
        CoreEntitlement.IsFreeTodayProvider = key => key == freeKey;
        shell.Show();
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var play = shell.Named<global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>("PlayTab")!;
        var band = play.FindControl<global::Avalonia.Controls.Border>("PlayLockDtrh")!;
        var badge = play.FindControl<global::ConditioningControlPanel.Avalonia.Controls.TierBadge>("PlayBadgeDtrh")!;
        var button = play.FindControl<global::Avalonia.Controls.Button>("BtnPlayDtrh")!;
        var emi = global::ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiTargets.Find("dtrh")!;
        var launcher = LauncherWindow.Destinations["dtrh"];
        Assert.Equal(1, badge.Tier);

        void Check(bool locked, bool gift)
        {
            play.RefreshPlayCards();
            Assert.Equal(locked, band.IsVisible);
            Assert.Equal(gift, badge.FreeToday);
            Assert.Equal(locked, GameWindow.DtrhLocked());
            Assert.Equal(locked, launcher.Locked());
            Assert.Equal(locked, emi.IsLocked());
        }

        freeKey = "remote"; // a different drop must not open this door
        Check(locked: true, gift: false);
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Single(denied);
        emi.Open();
        Assert.Equal(2, denied.Count);
        Assert.False(GameWindow.IsAnyOpen());

        freeKey = "dtrh";
        Check(locked: false, gift: true);
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        var w = Assert.IsType<GameWindow>(GameWindow.OpenForTests("dtrh"));
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        void EndRun()
        {
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
        }
        EndRun();
        var giftPay = Assert.Single(sent);
        Assert.True((bool)giftPay["daily_gift"]!);
        Assert.Equal("basic", (string?)giftPay["plan"]);
        sent.Clear();
        premium = true; // Basic owns it even on the drop day
        Check(locked: false, gift: false);
        EndRun();
        var ownedPay = Assert.Single(sent);
        Assert.False((bool)ownedPay["daily_gift"]!);
        Assert.Equal(JTokenType.Null, ownedPay["plan"]!.Type);
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Assert.False(GameWindow.IsAnyOpen());

        freeKey = null;
        Check(locked: false, gift: false); // Basic without Lab still opens
        emi.Open();
        Assert.NotNull(GameWindow.OpenForTests("dtrh"));
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Dtrh_FunnelOpensOnlyOutsideTheRunAndExit() => RunDtrh(lab: true, (shell, _, _, _) =>
    {
        FunnelPageMessage.ResetForTests();
        var opened = new List<string>();
        int counts = 0;
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        bool offline = CoreSettings.Current.OfflineMode;
        try
        {
            CoreSettings.Current.OfflineMode = false;
            FunnelPageMessage.BaseUrlOverride = "http://127.0.0.1:9";
            FunnelPageMessage.SendOverride = (_, _) => { Interlocked.Increment(ref counts); return Task.CompletedTask; };
            FunnelPageMessage.OpenCheckout = url => { opened.Add(url); return true; };
            FunnelPageMessage.UtcNow = () => now;
            FunnelPageMessage.RefreshAccount = () => Task.CompletedTask;
            var w = GameWindow.Launch("dtrh")!;
            // The exact string planCard.js hands WebKitGTK's invokeCSharpAction (plan-card.test.mjs asserts it).
            const string cta = "{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"dtrh\",\"b\":\"patreon\",\"plan\":\"basic\"}";
            w.HandleMessage(cta);
            Assert.Equal(ConditioningControlPanel.Services.Vault.PlanCatalog.Current.Basic.PatreonUrl, Assert.Single(opened));
            w.HandleMessage(cta); // throttled, but counted
            w.HandleMessage("{\"type\":\"run-started\"}");
            now = now.AddSeconds(4);
            w.HandleMessage(cta); // during a run
            Assert.Single(opened);
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            w.HandleMessage(cta);
            Assert.Equal(2, opened.Count);
            w.HandleMessage("{\"type\":\"exit\"}");
            now = now.AddSeconds(4);
            w.HandleMessage(cta); // page is exiting
            Assert.Equal(2, opened.Count);
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            w.HandleMessage(cta); // the window is closed
            Assert.Equal(2, opened.Count);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref counts) == 6, TimeSpan.FromSeconds(5)));
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally
        {
            CoreSettings.Current.OfflineMode = offline;
            FunnelPageMessage.ResetForTests();
        }
    });

    /// <summary>WPF DtrhHostService.PostManifest / OnRemoteCopiesReady (724ac82bc): the manifest's urls are root-relative
    /// (hostMedia.js counts only those, not 127.0.0.1, as local); with online media on, copies landing in
    /// <c>&lt;assets&gt;/.temp</c> re-post the manifest with them as asset urls; a closed window stops listening.</summary>
    [Fact]
    public void Dtrh_OnlineCopiesLand_TheManifestIsRepostedWithThemAsAssetUrls() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.MediaSource, s.RemoteMediaConsented, s.RemoteMediaRatio, DtrhRemoteMedia.Fetch, GameWindow.DtrhCopier);
        var cachePath = Path.Combine(CorePaths.UserData, "dtrh_remote_media.json");
        try
        {
            File.Delete(cachePath);
            DtrhRemoteMedia.ResetForTests();
            (s.MediaSource, s.RemoteMediaConsented, s.RemoteMediaRatio) = ("mixed", true, 40);
            DtrhRemoteMedia.Fetch = (kind, _) => Task.FromResult((kind != DtrhRemoteMedia.Kind.Stills ? new List<global::ConditioningControlPanel.Services.Fyp.FypAssetManifest.Entry>()
                : new() { new() { Id = "scrolller/Sub/7", Url = "https://cdn/7.webp", Type = "image", Origin = "online" } }, (string?)null));
            GameWindow.DtrhCopier = new((url, _) =>
            {
                var dir = Directory.CreateDirectory(Path.Combine(CorePaths.EffectiveAssets, ".temp")).FullName;
                var path = Path.Combine(dir, DtrhRemoteMedia.CopyPrefix + "7.webp");
                File.WriteAllBytes(path, new byte[] { 1 });
                return Task.FromResult<string?>(path);
            }, _ => { });

            var w = GameWindow.Launch("dtrh")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            var first = sent.Single(o => (string?)o["type"] == "manifest");
            Assert.Equal("/ccp.assets/images/pic.png", (string?)first["images"]!.Single()["url"]);   // cold cache: one launch behind
            Assert.Equal(1, DtrhRemoteMedia.CopiesReadyListeners);

            // Loops come back dry, so stills are fetched next (the 30 min back-off), copied and re-posted.
            DtrhRemoteMedia.RefillTask.Wait(TimeSpan.FromSeconds(10));
            sent.Clear();
            w.HandleMessage("{\"type\":\"ready\"}");
            DtrhRemoteMedia.RefillTask.Wait(TimeSpan.FromSeconds(10));
            DtrhRemoteMedia.CopyTask.Wait(TimeSpan.FromSeconds(10));
            sent.Clear();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!sent.Any(o => (string?)o["type"] == "manifest") && DateTime.UtcNow < deadline) Dispatcher.UIThread.RunJobs();
            var again = sent.Single(o => (string?)o["type"] == "manifest");
            var images = again["images"]!.Select(e => ((string?)e["name"], (string?)e["url"])).ToList();
            Assert.Contains(("pic.png", "/ccp.assets/images/pic.png"), images);                       // mixed: the library stays
            Assert.Contains(("online40:Sub-7.webp", "/ccp.assets/.temp/ccp_temp_remote_7.webp"), images);

            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, DtrhRemoteMedia.CopiesReadyListeners);
        }
        finally
        {
            (s.MediaSource, s.RemoteMediaConsented, s.RemoteMediaRatio, DtrhRemoteMedia.Fetch, GameWindow.DtrhCopier) = old;
            DtrhRemoteMedia.ResetForTests();
            File.Delete(cachePath);
            File.Delete(Path.Combine(CorePaths.EffectiveAssets, ".temp", DtrhRemoteMedia.CopyPrefix + "7.webp"));
        }
    });

    /// <summary>WPF MainWindow.xaml.cs:1505 TryRacePauseOnEscape (DtRH since 2026-10-12): with Escape as the panic
    /// key and the game in front, the first press is the game's pause (kept-escape to the page, the game stays
    /// open, nothing exits); a second press inside 2 s is a full panic that closes it (PanicSurfaces "games").</summary>
    [Fact]
    public void Dtrh_EscapePauses_ASecondEscapeInsideTwoSecondsClosesIt() => RunDtrh(lab: true, (shell, _, _, _) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled) = (s.PanicKey, s.PanicKeyEnabled);
        try
        {
            s.PanicKey = "Escape";
            s.PanicKeyEnabled = true;
            var shellClosed = false;
            shell.Closed += (_, _) => shellClosed = true;
            var w = GameWindow.Launch("dtrh")!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(w, GameWindow.DtrhInFront());
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            sent.Clear();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

            shell.HandlePanicKeyPress(t0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());                                 // a pause, not a close
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);

            shell.HandlePanicKeyPress(t0.AddSeconds(1));                          // again within 2 s: panic
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen()); // WPF gives the page its settlement window
            Assert.Equal("end-run", (string?)sent.Last()["type"]);
            Assert.Equal("host", (string?)sent.Last()["reason"]);
            Assert.False(w.KeptEscapeReady);
            w.HandleMessage("{\"type\":\"exit-done\"}");
            Assert.False(GameWindow.IsAnyOpen());
            Assert.False(shellClosed);                                            // a game owned the screen: no exit rung
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });

    /// <summary>WPF DtrhHostService.cs:250-266,1017-1028: final page booking wins over
    /// the older progress snapshot; no response forces close after 1200 ms.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dtrh_PanicSettlesOrTimesOut(bool responds) => RunDtrh(lab: true, (_, xp, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"run-started\"}");
        w.HandleMessage("{\"type\":\"run-progress\",\"score\":10,\"durationSec\":180}");
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        GameWindow.CloseAllForPanic();
        Assert.True(GameWindow.IsAnyOpen());
        Assert.False(w.DtrhReady);
        Assert.Equal("end-run", (string?)Assert.Single(sent)["type"]);
        Assert.Equal("host", (string?)sent[0]["reason"]);
        Assert.Empty(xp);
        if (responds)
        {
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            w.HandleMessage("{\"type\":\"exit-done\"}");
            Assert.Equal(300.0, Assert.Single(xp));
        }
        else
        {
            // RunJobs drains queued work but does not service native dispatcher timers.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            w.Closed += (_, _) => deadline.Cancel();
            Dispatcher.UIThread.MainLoop(deadline.Token);
            Assert.InRange(clock.ElapsedMilliseconds, 1100, 2999);
            Assert.Single(xp); // forced close still banks the last snapshot
        }
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Single(xp);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dtrh_PanicClosesBootingOrAlreadyExitingPage(bool ready) => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        if (ready)
        {
            w.HandleMessage("{\"type\":\"ready\"}");
            GameWindow.CloseAllForPanic();
            Assert.True(GameWindow.IsAnyOpen());
        }
        GameWindow.CloseAllForPanic();
        Assert.False(GameWindow.IsAnyOpen());
    });

    /// <summary>WPF DtrhHostService.cs:266: a failed end-run post must still close the window.</summary>
    [Fact]
    public void Dtrh_PanicClosesIfTheBridgeThrows() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        w.HandleMessage("{\"type\":\"ready\"}");
        w.Posted += _ => throw new InvalidOperationException("bridge failed");
        GameWindow.CloseAllForPanic();
        Assert.False(GameWindow.IsAnyOpen());
    });

    /// <summary>WPF TryRacePauseOnEscape (owner 2026-10-12: Escape only pauses a game) on every game this head
    /// hosts, and main 251b60807: a page that has not said ready (or is on its way out) cannot pause, so its
    /// Escape is a full panic that closes it. A second Escape inside 2 s is a full panic too.</summary>
    [Fact]
    public void Breakout_EscapePausesOnlyOnceTheGameIsUp_ASecondEscapeClosesIt() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled) = (s.PanicKey, s.PanicKeyEnabled);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled) = ("Escape", true);
            launcher.Hide();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

            // Booting: the page never said ready, so the press is a full panic, never a swallowed pause.
            var w = GameWindow.Launch("breakoutdemo")!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(w, GameWindow.EscapePauseInFront());
            shell.HandlePanicKeyPress(t0);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());

            // Up: the first Escape is the pause, handed to the page as kept-escape; the window stays.
            w = GameWindow.Launch("breakoutdemo")!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"ready\"}");
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            var t1 = t0.AddSeconds(10);
            shell.HandlePanicKeyPress(t1);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);

            shell.HandlePanicKeyPress(t1.AddSeconds(1));                     // again within 2 s: panic
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });

    /// <summary>Each hosted game's kept-Escape page and frame (WPF KeptEscapeType per host: the board reads
    /// pbp:escape, the race / Breakout / DtRH pages kept-escape). FYP and Just Drop do not pause on Escape.</summary>
    [Theory]
    [InlineData("race", "Glassroad", "kept-escape")]
    [InlineData(GameWindow.PbpId, "Board", "pbp:escape")]
    [InlineData("breakoutdemo", "Breakout", "kept-escape")]
    [InlineData(GameWindow.DtrhId, "Dtrh", "kept-escape")]
    [InlineData(GameWindow.GlassroadId, "Glassroad", "kept-escape")]
    [InlineData(GameWindow.PrizeCounterId, "Counter", "kept-escape")]
    [InlineData(GameWindow.FypId, "None", null)]
    [InlineData(GameWindow.JustDropId, "None", null)]
    public void EveryHostedGame_KnowsItsKeptEscapePage(string id, string page, string? frame) => Run((_, launcher) =>
    {
        launcher.Hide();
        var w = new GameWindow(GameWindow.Games[id]);
        try
        {
            Assert.Equal(page, w.KeptEscapePage.ToString());
            Assert.False(w.KeptEscapeReady);                                // nothing loaded: never ready
            if (frame == null) return;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.PostKeptEscape();
            Assert.Equal(frame, (string?)Assert.Single(sent)["type"]);
        }
        finally { w.Close(); }
    });
    [Fact]
    public void Dtrh_Heartbeat_RelaunchesOnceAndStopsWatching() => RunDtrh(lab: true, (_, xp, _, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.DtrhUtcNow = () => now;
        var w = GameWindow.Launch("dtrh")!;
        Assert.True(w.DtrhWatchRunning);
        Assert.Equal("idle", w.CheckDtrhHeartbeat(now.AddMinutes(1))); // loading is not a vanished page
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal("ok", w.CheckDtrhHeartbeat(now.AddSeconds(20))); // the hub's strict deadline
        w.Web.OnWebMessage("{\"type\":\"run-started\"}");
        Assert.Equal("ok", w.CheckDtrhHeartbeat(now.AddSeconds(10)));
        w.HandleMessage("{\"type\":\"run-progress\",\"score\":100,\"durationSec\":120}");
        now = now.AddSeconds(11);
        Assert.Equal("recovering", w.CheckDtrhHeartbeat(now));
        Assert.Equal("recovering", w.CheckDtrhHeartbeat(now)); // no second queued recovery
        Dispatcher.UIThread.RunJobs();
        var replacement = GameWindow.OpenForTests("dtrh");
        Assert.NotNull(replacement);
        Assert.NotSame(w, replacement);
        Assert.Single(xp); // recovery uses the existing abandoned-run booking exactly once
        Assert.False(w.DtrhWatchRunning);
        Assert.True(replacement.DtrhWatchRunning);
        replacement.Web.OnNavigationCompleted(replacement.PageUrl);
        replacement.Web.OnWebMessage("{\"type\":\"ready\"}");
        replacement.Web.OnWebMessage("{\"type\":\"heartbeat\"}");
        Assert.Equal("ok", replacement.CheckDtrhHeartbeat(now.AddSeconds(20)));
        now = now.AddSeconds(21);
        Assert.Equal("recovering", replacement.CheckDtrhHeartbeat(now));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(replacement.DtrhWatchRunning);
        Assert.True(GameWindow.DtrhRelaunchedOnce);

        // The once-per-session budget is not refilled by manually reopening the game.
        var reopened = GameWindow.Launch("dtrh")!;
        reopened.Web.OnNavigationCompleted(reopened.PageUrl);
        reopened.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal("recovering", reopened.CheckDtrhHeartbeat(now.AddSeconds(21)));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Theory]
    [InlineData("heartbeat")]
    [InlineData("pong")]
    public void Dtrh_Heartbeat_BeatsRefreshDeadline_ExitAndQueuedPanicCannotRelaunch(string frame) => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.DtrhUtcNow = () => now;
        var w = GameWindow.Launch("dtrh")!;
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        now = now.AddSeconds(19);
        w.Web.OnWebMessage(new JObject { ["type"] = frame }.ToString());
        Assert.Equal("ok", w.CheckDtrhHeartbeat(now.AddSeconds(20)));
        w.Web.OnWebMessage("{\"type\":\"exit\"}");
        Assert.Equal("idle", w.CheckDtrhHeartbeat(now.AddMinutes(1)));
        w.Web.OnWebMessage("{\"type\":\"exit-done\"}");
        Assert.False(w.DtrhWatchRunning);

        w = GameWindow.Launch("dtrh")!;
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal("recovering", w.CheckDtrhHeartbeat(now.AddSeconds(21)));
        GameWindow.CloseAllForPanic(); // recovery is queued, not permitted to resurrect this surface
        Dispatcher.UIThread.RunJobs();
        // WPF DtrhHostService.cs:250-266: panic asks the ready page to settle; its exit-done closes the window.
        w.Web.OnWebMessage("{\"type\":\"exit-done\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.DtrhWatchRunning);
        Assert.False(GameWindow.DtrhRelaunchedOnce);
        Assert.Equal("idle", w.CheckDtrhHeartbeat(now.AddMinutes(2)));
    });

    [Fact]
    public void Dtrh_Heartbeat_SlowReadyRestartsTheClock() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        // WPF OnPageReady (DtrhHostService.cs:275): a 25 s page load must not spend the hub's 20 s deadline.
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.DtrhUtcNow = () => now;
        var w = GameWindow.Launch("dtrh")!;
        now = now.AddSeconds(25);
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal("ok", w.CheckDtrhHeartbeat(now));
        Assert.True(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Dtrh_Heartbeat_PanicDuringRecoveryCloseCannotRelaunch() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.DtrhUtcNow = () => now;
        var w = GameWindow.Launch("dtrh")!;
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        w.Closed += (_, _) => GameWindow.CloseAllForPanic();
        Assert.Equal("recovering", w.CheckDtrhHeartbeat(now.AddSeconds(21)));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.DtrhWatchRunning);
    });

    [Fact]
    public void Dtrh_Heartbeat_FailedRelaunchDoesNotEscapeTheDispatcher() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.DtrhUtcNow = () => now;
        var w = GameWindow.Launch("dtrh")!;
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        var oldServer = GameWindow.Server;
        try
        {
            GameWindow.Server = () => throw new IOException("test recovery server failure");
            Assert.Equal("recovering", w.CheckDtrhHeartbeat(now.AddSeconds(21)));
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.False(w.DtrhWatchRunning);
            Assert.True(GameWindow.DtrhRelaunchedOnce);
        }
        finally { GameWindow.Server = oldServer; }
    });
}
