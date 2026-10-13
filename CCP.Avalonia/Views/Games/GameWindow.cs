using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// One web game in a window of its own: the port's stand-in for WPF's per-game WebView2 hosts
    /// (BackRoomHostService, BreakoutHostService, PieceByPieceHostService, GoonHostService, ArcademyHostService,
    /// DtrhHostService), all of which load https://ccp.game/&lt;page&gt; through Chaos/ChaosWebViewHost.cs.
    /// Here the page comes off <see cref="WebAssetServer"/> into a <see cref="WebHost"/>, one live window per
    /// game, focused rather than relaunched, and every one of them is a panic surface (PanicSurfaces "games",
    /// WPF Services/Safety/GameSurfaces.cs:53-55).
    ///
    /// <para>The bridge is the shell protocol those pages share (WPF ChaosWebViewHost.cs:1719): the page says
    /// <c>ready</c>, the host answers <c>init</c> (language + identity + Breakout access), a station's
    /// <c>media-request</c> gets a library deal (<see cref="GameMediaManifest.Deal"/>), <c>fx</c> gets its
    /// <c>fx-ack</c>, and <c>exit</c> / <c>exit-done</c> / <c>boot-error</c> close it.
    /// Ported from trunk's Views/Games/GameWindow.cs with the Breakout demo and Piece by Piece (GameWindow.Pbp.cs) and Racing Thoughts (GameWindow.Race.cs) registered; every other
    /// game lands with its own frames.</para>
    /// </summary>
    internal sealed partial class GameWindow : Window
    {
        /// <summary>A game this head can open: its launcher id, title key, page under Resources/web, the
        /// WPF gate run before the window is built (null = free) and the page's query.</summary>
        internal sealed record Game(string Id, string TitleKey, string Page, Func<bool>? Gate = null, string? Query = null);

        /// <summary>WPF 7.1.5's game hosts, StartUrl for StartUrl.</summary>
        internal static readonly IReadOnlyDictionary<string, Game> Games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase)
        {
            // WPF BreakoutHostService.cs:8 StartUrl; LaunchDemo (:13) is free and needs no account. demo=1 holds the
            // page to 3 levels even before init lands (access.js reads it wherever chrome.webview is absent).
            ["breakoutdemo"] = new("breakoutdemo", "launcher_game_breakoutdemo_title", "backroom/stations/breakout/play.html", null, "desktop=1&demo=1"),
            // WPF FypHostService.cs:94 StartUrl, behind OpenFypFeed's premium gate (MainWindow.Lab.cs:246).
            [FypId] = new(FypId, "tab_fyp", "fyp/index.html", FypGate),
            // WPF JustDropHostService: the live shop at app.cclabs.app, not a page off disk (GameWindow.JustDrop.cs).
            [JustDropId] = new(JustDropId, "jd_door_title", "", JustDropDoorOpen),
            // WPF PieceByPieceHostService.cs:221 StartUrl; free for everyone since 2026-09-27 (:188), no gate.
            [PbpId] = new(PbpId, "launcher_game_piecebypiece_title", "piecebypiece/index.html"),
            // WPF CaucusHostService.Launch: dtrh/race.html behind the racing-purchase door (GameWindow.Race.cs).
            ["race"] = new("race", "launcher_game_race_title", "dtrh/race.html", RaceGate),
        };

        private static readonly List<GameWindow> Open = new();

        /// <summary>Where pages come from; tests point it at a throwaway root.</summary>
        internal static Func<WebAssetServer> Server { get; set; } = () => WebAssetServer.Shared;

        internal Game Spec { get; }
        internal WebHost Web { get; }
        internal Uri? PageUrl { get; private set; }
        internal bool IsReady { get; private set; }
        private bool _closing;

        /// <summary>WPF LauncherHost.LaunchGame -> each host's LaunchCore: one live window per game, focused
        /// rather than relaunched (a second click mid-match must never restart it). Null when refused.</summary>
        internal static GameWindow? Launch(string id)
        {
            if (!Games.TryGetValue(id, out var spec)) { Log.Warning("[Game] no game {Id}", id); return null; }
            GameWindow? live;
            lock (Open) live = Open.FirstOrDefault(w => string.Equals(w.Spec.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
            if (live != null) { live.Activate(); return live; }
            try { if (spec.Gate != null && !spec.Gate()) return null; }
            catch (Exception ex) { Log.Warning(ex, "[Game] gate for {Id} threw; refused", id); return null; }
            var window = new GameWindow(spec);
            window.Load(Server());
            window.Show();
            return window;
        }

        /// <summary>--render-all's sample: the Breakout demo's frame with no page loaded.</summary>
        internal GameWindow() : this(Games["breakoutdemo"]) { }

        internal GameWindow(Game spec)
        {
            Spec = spec;
            Web = new WebHost();
            Title = Loc.Get(spec.TitleKey);
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = Web;
            Web.WebMessage += OnPageMessage;
            // The page never leaves the served origin; anything else is refused before the engine loads it.
            Web.AllowNavigation = url => PageUrl != null && SameOrigin(url, PageUrl);
            Opened += (_, _) => { lock (Open) Open.Add(this); if (IsRace) StartRace(); };
            Closing += (_, _) => _closing = true;
            Closed += (_, _) => { lock (Open) Open.Remove(this); DisposeRace(); };
            if (spec.Id == PbpId) Opened += (_, _) => OpenPbp();
        }

        private WebAssetServer? _server;
        private Lazy<GameMediaManifest.Manifest>? _library;   // single-flight: concurrent sit-downs share one walk

        internal void Load(WebAssetServer server)
        {
            if (Spec.Id == JustDropId) { LoadJustDrop(); return; }
            _server = server;
            PageUrl = new Uri(server.Url(Spec.Page, Spec.Query));
            Web.Navigate(PageUrl);
        }

        internal static bool SameOrigin(Uri a, Uri b) =>
            a.IsAbsoluteUri && b.IsAbsoluteUri && a.Scheme == b.Scheme && a.Authority == b.Authority;

        /// <summary>WPF ChaosWebViewHost.OnWebMessage's same-document guard.</summary>
        private void OnPageMessage(string json)
        {
            if (PageUrl == null || Web.CurrentUrl == null || !SameOrigin(Web.CurrentUrl, PageUrl)) return;
            HandleMessage(json);
        }

        /// <summary>The shared shell frames (WPF ChaosWebViewHost.OnWebMessage + each host's OnPageReady).</summary>
        internal void HandleMessage(string json)
        {
            JObject o;
            try { o = JObject.Parse(Unwrap(json)); }
            catch (Exception ex) { Log.Debug("[Game] {Id}: unreadable page message: {E}", Spec.Id, ex.Message); return; }
            if (Spec.Id == FypId && HandleFyp(o)) return;
            if (Spec.Id == JustDropId) { HandleJustDrop(o); return; }   // the remote site never gets the games' frames
            if (Spec.Id == PbpId && HandlePbp(o)) return;
            if (IsRace && HandleRace(o)) return;
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    Post(InitMessage());   // WPF BackRoomHostService.BuildInit (:566); no manifest frame for Breakout
                    break;
                case "media-request":
                    // WPF BackRoomBridge.cs:208 -> OnMediaRequestAsync: the station's pictures for this sit-down.
                    // The walk touches every library file, so it runs off the UI thread (WPF OffUi); the reply is posted back.
                    var reqId = (string?)o["reqId"];
                    int count = GameMediaManifest.MediaCount(o["count"]?.Type == JTokenType.Integer ? (int?)o["count"] : null);
                    // The library is walked once per window, like WPF's FlashService snapshot, not on every sit-down.
                    var server = _server;
                    var library = _library ??= new(() => server == null ? new() : GameMediaManifest.BuildLocal(server));
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        object deal;
                        try { deal = GameMediaManifest.Deal(library.Value, reqId, Random.Shared.Next(), count); }
                        catch (Exception ex) { Log.Debug("[Game] media deal failed: {E}", ex.Message); deal = GameMediaManifest.Deal(new(), reqId, 0, count); }
                        Dispatcher.UIThread.Post(() => Post(deal));
                    });
                    break;
                case "fx":
                    // WPF BackRoomBridge.OnFx (:479) answers every fx with one fx-ack, or the page waits out its 4 s
                    // timeout. The five big-beat ids are drawn in the page (page-fx.js); this head has no desktop
                    // primitives for the rest yet, so they ack skipped "unknown" (BackRoomFxSkipReason.Unknown).
                    Post(new
                    {
                        type = "fx-ack", token = (string?)o["token"], fired = Array.Empty<string>(),
                        skipped = new[] { new { prim = (string?)o["fxId"] ?? "", why = "unknown" } },
                    });
                    break;
                case "log":
                    Log.Debug("[Game] {Id} page: {Msg}", Spec.Id, (string?)o["msg"]);
                    break;
                case "boot-error":
                    Log.Warning("[Game] {Id}: page boot-error: {Msg}", Spec.Id, (string?)o["msg"]);
                    Close();
                    break;
                case "exit":
                    // The page winds down and answers exit-done; WPF's ArmExitWatchdog closes it anyway.
                    DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(1200));
                    break;
                case "exit-done":
                case "close":
                    Close();
                    break;
                default:
                    Log.Debug("[Game] {Id}: unhandled page message '{Type}'", Spec.Id, (string?)o["type"]);
                    break;
            }
        }

        /// <summary>The engine may hand a string-carrier message over JSON-quoted.</summary>
        internal static string Unwrap(string body)
        {
            var t = body.Trim();
            if (t.Length > 1 && t[0] == '"')
            {
                try { return JsonConvert.DeserializeObject<string>(t) ?? body; } catch { }
            }
            return body;
        }

        /// <summary>The init every one of these pages reads defensively: language + identity, plus the
        /// Breakout access WPF BackRoomHostService.BuildInit (:566) sends.</summary>
        internal object InitMessage() => new
        {
            type = "init",
            protocol = 1,
            lang = CoreSettings.Current.Language,
            solo = !CoreAccount.IsLoggedIn,
            identity = new
            {
                unifiedId = CoreAccount.UnifiedUserId ?? "",
                displayName = CoreAccount.DisplayName ?? "",
                appVersion = CoreReleaseContent.AppVersion,
            },
            breakout = BreakoutEntitlementFor(Spec.Id),
            breakoutStandalone = Spec.Id.StartsWith("breakout", StringComparison.OrdinalIgnoreCase),
        };

        /// <summary>WPF BreakoutAccess.Current(demo): the demo door is 3 levels, whatever the tier.</summary>
        internal static object? BreakoutEntitlementFor(string id) =>
            id.Equals("breakoutdemo", StringComparison.OrdinalIgnoreCase)
                ? new { storyLimit = 3, endless = false, demo = true }
                : null;

        /// <summary>Every host -&gt; page frame, as sent (tests read the bridge from here).</summary>
        internal event Action<string>? Posted;

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post) through the page's string carrier.</summary>
        internal void Post(object message)
        {
            var json = JsonConvert.SerializeObject(message);
            Posted?.Invoke(json);
            if (_closing) return;
            _ = Web.PostJsonAsync(json);
        }

        // ---- panic (PanicSurfaces "games", WPF GameSurfaces.cs:53-55 -> CloseActive("panic")) ----

        internal static bool IsAnyOpen() { lock (Open) return Open.Count > 0; }

        internal static GameWindow[] OpenWindowsForTests() { lock (Open) return Open.ToArray(); }

        internal static GameWindow? OpenForTests(string id) => OpenWindowsForTests().FirstOrDefault(w => w.Spec.Id == id);

        internal static void CloseAllForPanic()
        {
            GameWindow[] all;
            lock (Open) all = Open.ToArray();
            foreach (var w in all)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Warning(ex, "[Game] panic close of {Id} failed", w.Spec.Id); }
            }
        }
    }
}
