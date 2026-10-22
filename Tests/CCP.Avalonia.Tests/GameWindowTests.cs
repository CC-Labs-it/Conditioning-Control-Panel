using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using ConditioningControlPanel.Services.Chaos;
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
            try { Assert.Empty(GameMediaManifest.BuildLocal(server.AssetUrl).Videos); }
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
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = t0;
        GameWindow.RaceUtcNow = () => now;                // stepped clock (P08); RunRace restores it
        var w = GameWindow.Launch("race")!;
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
            var w = GameWindow.Launch("race")!;
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
            var w = GameWindow.Launch("race")!;
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
            var w = GameWindow.Launch("race")!;
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
        var (oldLab, oldFree, oldDenied, oldBank, oldXp, oldAssets, oldClock) = (CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider,
            CoreEntitlement.ShowDeniedHandler, GameWindow.DtrhBankSparks, CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider, GameWindow.DtrhUtcNow);
        var (oldRuns, oldForce) = (ChaosMeta.State.RunsCompleted, ChaosMeta.State.ForceScriptedRun);
        var lib = Directory.CreateTempSubdirectory("dtrh-lib-").FullName;
        var xp = new List<double>();
        var runs = new List<bool>();
        var denied = new List<string>();
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => lab;
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
            (CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler, GameWindow.DtrhBankSparks,
                CoreProgression.AddXPProvider, CorePaths.EffectiveAssetsProvider, GameWindow.DtrhUtcNow) = (oldLab, oldFree, oldDenied, oldBank, oldXp, oldAssets, oldClock);
            (ChaosMeta.State.RunsCompleted, ChaosMeta.State.ForceScriptedRun) = (oldRuns, oldForce);
            GameWindow.CloseAllForPanic();
            Directory.Delete(lib, true);
        }
    });

    /// <summary>WPF LauncherCatalogue :241 (Lab door, DemandLab refusal), OnPageReady :271 (init with motionLevel
    /// and runSetup, then the manifest with videos), OnRequestRun :523, OnRunEnded :583 (one payout per descent)
    /// and DisposeAll :1002 (a window closed mid-descent banks its last snapshot, paid by the host's clock).</summary>
    [Fact]
    public void Dtrh_LabDoor_BootRunLifecycle_AndCloseBanks()
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
            Assert.Equal(new[] { "init", "manifest" }, sent.Select(o => (string?)o["type"]));
            Assert.Equal("reduced", (string?)sent[0]["motionLevel"]);
            Assert.Equal("Normal", (string?)sent[0]["runSetup"]!["difficulty"]);
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
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");   // a second end pays nothing
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
            Assert.False(GameWindow.IsAnyOpen());
            Assert.False(shellClosed);                                            // a game owned the screen: no exit rung
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });
}
