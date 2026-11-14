using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.JustDrop;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.PrizeCounter;
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
    /// Ported from trunk's Views/Games/GameWindow.cs with the Breakout demo and Piece by Piece (GameWindow.Pbp.cs), Racing Thoughts (GameWindow.Race.cs) and Down the Rabbit Hole (GameWindow.Dtrh.cs) registered; every other
    /// game lands with its own frames.</para>
    /// </summary>
    internal sealed partial class GameWindow : Window
    {
        /// <summary>A game this head can open: its launcher id, title key, page under Resources/web, the
        /// WPF gate run before the window is built (null = free) and the page's query.</summary>
        internal sealed record Game(string Id, string TitleKey, string Page, Func<bool>? Gate = null, string? Query = null);

        /// <summary>WPF 7.1.5's game hosts, StartUrl for StartUrl.</summary>
        internal static readonly Dictionary<string, Game> Games = BuildGames();

        private static Dictionary<string, Game> BuildGames()
        {
            var games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase)
        {
            // WPF BreakoutHostService.cs:8 StartUrl; LaunchDemo (:13) is free and needs no account. demo=1 holds the
            // page to 3 levels even before init lands (access.js reads it wherever chrome.webview is absent).
            ["breakout"] = new("breakout", "launcher_game_breakout_title", "backroom/stations/breakout/play.html", BreakoutGate, "desktop=1"),
            [BreakoutDemoId] = new(BreakoutDemoId, "launcher_game_breakoutdemo_title", "backroom/stations/breakout/play.html", null, "desktop=1&demo=1"),
            // WPF FypHostService.cs:94 StartUrl, behind OpenFypFeed's premium gate (MainWindow.Lab.cs:246).
            [FypId] = new(FypId, "tab_fyp", "fyp/index.html", FypGate),
            // WPF JustDropHostService: the live shop at app.cclabs.app, not a page off disk (GameWindow.JustDrop.cs).
            [JustDropId] = new(JustDropId, "jd_door_title", "", JustDropDoorOpen),
            // WPF PieceByPieceHostService.cs:221 StartUrl; free for everyone since 2026-09-27 (:188), no gate.
            [PbpId] = new(PbpId, "launcher_game_piecebypiece_title", "piecebypiece/index.html"),
            // WPF #3126: the 2D bridge remains, but no production door opens it.
            ["loom"] = new("loom", "billboard_loom_title", "dtrh/loom.html"), // WPF LoomHostService: free
            ["race"] = new(GlassroadId, "launcher_game_race_title", "glassroad/index.html", GlassroadGate),
            // WPF DtrhHostService.cs:196 StartUrl behind the Basic door (GameWindow.Dtrh.cs).
            [DtrhId] = new(DtrhId, "launcher_game_dtrh_title", "dtrh/index.html", DtrhGate),
            // WPF #3132: Racing Thoughts is Prime only, including solo.
            [GlassroadId] = new(GlassroadId, "launcher_game_race_title", "glassroad/index.html", GlassroadGate),
            // WPF PrizeCounterHostService.cs:28 StartUrl; no tile, its doors call LaunchPrizeCounter (GameWindow.PrizeCounter.cs).
            [PrizeCounterId] = new(PrizeCounterId, "shop_title", "prizecounter/index.html"),
        };

            RegisterGoon(games);
            RegisterBackRoom(games);
            RegisterArcademy(games);
            return games;
        }

        static partial void RegisterGoon(Dictionary<string, Game> games);
        static partial void RegisterBackRoom(Dictionary<string, Game> games);
        static partial void RegisterArcademy(Dictionary<string, Game> games);
        partial void HandleGoonMessage(JObject message, ref bool handled);
        partial void HandleBackRoomMessage(JObject message, ref bool handled);
        partial void HandleArcademyMessage(JObject message, ref bool handled);

        internal const string BreakoutDemoId = "breakoutdemo";

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
        internal static GameWindow? Launch(string id, Windows.MainShellWindow? panel = null)
        {
            id = ResolveDoor(id);   // one routing seam for the Racing Thoughts host
            if (!Games.TryGetValue(id, out var spec)) { Log.Warning("[Game] no game {Id}", id); return null; }
            if (spec.Id == JustDropId && !JustDropDoorOpen()) return null;
            return spec.Id == GlassroadId ? LaunchRacingThoughts() : LaunchWindow(spec, panel: panel);
        }

        /// <summary>A friend's Racing Thoughts invite: the same race gate and window, plus the room to join
        /// (WPF FriendsLanding.cs:568 -> GlassroadHostService.Launch(joinRoomId)).</summary>
        internal static GameWindow? LaunchFriendRace(string? room) => LaunchRacingThoughts(room);

        /// <summary>The race launch seam; on Linux LaunchWindow tries the owned shell first (GameWindow.FirstRace.cs).
        /// WPF GlassroadHostService.Launch: DemandPrime runs before a live window is re-focused, so a tier
        /// loss cannot bring a live race forward through another door.</summary>
        private static GameWindow? LaunchRacingThoughts(string? room = null)
        {
            var spec = Games[GlassroadId];
            return PassesGate(spec) ? LaunchWindow(spec, gated: true, room) : null;
        }

        private static bool PassesGate(Game spec)
        {
            try { return spec.Gate == null || spec.Gate(); }
            catch (Exception ex) { Log.Warning(ex, "[Game] gate for {Id} threw; refused", spec.Id); return false; }
        }

        private static GameWindow? LaunchWindow(Game spec, bool gated = false, string? room = null,
            Windows.MainShellWindow? panel = null)
        {
            GameWindow? live;
            lock (Open) live = Open.FirstOrDefault(w => string.Equals(w.Spec.Id, spec.Id, StringComparison.OrdinalIgnoreCase)
                || ((spec.Id is "breakout" or "breakoutdemo") && w.IsBreakout));
            if (live != null)
            {
                // WPF BreakoutHostService.LaunchFull gates even a focus of the live game.
                if (spec.Id == "breakout" && !BreakoutGate()) return null;
                live.HandoffRaceRoom(room);
                // #4286: a feed the WM hid (ghost restored on hide) comes back with its door.
                if (live.Spec.Id == FypId && !live.IsVisible) live.Show();
                if (live.RaceShell != null) live.RaceShell.Focus(); else live.Activate();   // the shell's window is never shown
                // WPF PieceByPieceHostService.cs:165: reopening a live board returns its keyboard too.
                if (live.Spec.Id == PbpId)
                    Dispatcher.UIThread.Post(() => { if (!live._closing) live.Web.FocusPage(); }, DispatcherPriority.Loaded);
                if (live.IsBreakout && live.Spec.Id != spec.Id)
                {
                    _ = live.SwitchBreakoutAsync(spec.Id);
                    return null; // the existing run stays alive until the user answers
                }
                return live;
            }
            // JustDrop's door ran in Launch (before any refocus); the race gated before its refocus.
            if (!gated && spec.Id != JustDropId && !PassesGate(spec)) return null;
            // WPF PieceByPieceHostService.cs:165-168: refocus is free; failed setup still counts.
            if (spec.Id == PbpId)
            {
                try { SeasonFeatureTracking.Track(CoreSettings.Current, ConditioningControlPanel.Models.SeasonFeatureKeys.PieceByPiece,
                    ev => App.FeatureDayLog?.Note(ev), DateTime.UtcNow); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            if (spec.Id == FypId) OpenFypVisit();
            try
            {
                var window = new GameWindow(spec);
                if (spec.Id == GlassroadId && UseLinuxRaceCard())
                {
                    var raceShell = window.StartRaceShell(room);   // B2 (#3702): the owned Electron shell first
                    if (raceShell == true) return window;
                    window.ShowRaceCard(room, failed: raceShell == false);
                }
                else window.Load(Server(), room);
                window.Show();
                // WPF ChaosWebViewHost.cs:904-1013 AttachMainWindowGlue: the shop window is owned by the panel.
                if (spec.Id == JustDropId && (panel ?? Windows.MainShellWindow.Current) is { } shell)
                    window.AttachJustDropOwner(shell);
                return window;
            }
            catch
            {
                if (spec.Id == FypId) CloseFypVisit();
                throw;
            }
        }

        /// <summary>--render-all's sample: the Breakout demo's frame with no page loaded.</summary>
        internal GameWindow() : this(Games[BreakoutDemoId]) { }

        internal GameWindow(Game spec)
        {
            Spec = spec;
            Web = new WebHost();
            if (spec.Id == FypId) Web.BrowserProcessFailed += () => { if (!_closing) Close(); };
            if (spec.Id == "loom") Web.EnvironmentRequested += ConfigureLoomEnvironment;
            // Before Load/Show can create the native environment (WPF Pbp host :200-215).
            if (spec.Id == PbpId) Web.EnvironmentRequested += ConfigurePbpEnvironment;
            Title = spec.Id == "loom" ? "The Loom" : Loc.Get(spec.TitleKey);
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = Web;
            Web.WebMessage += OnPageMessage;
            PropertyChanged += (_, e) =>
            {
                if (!IsBreakout || e.Property != WindowStateProperty) return;
                RoomMinimizedChanged(WindowState == WindowState.Minimized);
            };
            // The page never leaves the served origin; anything else is refused before the engine loads it.
            Web.AllowNavigation = url => PageUrl != null && SameOrigin(url, PageUrl);
            if (IsDtrh)
            {
                Opened += (_, _) => CaptureDtrhWindowedFrame();
                PositionChanged += (_, _) => CaptureDtrhWindowedFrame();
                SizeChanged += (_, _) => CaptureDtrhWindowedFrame();
                PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) OnDtrhWindowStateChanged(); };
            }
            Opened += (_, _) => { lock (Open) { if (!Open.Contains(this)) Open.Add(this); } if (IsRace) StartRace(); if (IsDtrh) StartDtrh(); StartBreakoutAccessWatch(); };
            Activated += (_, _) => { FunnelPageMessage.OnAppActivated(); ResumeBreakoutAfterPanic(DateTime.Now); };   // WPF's Application.Activated covers every window
            Closing += (_, _) => { _closing = true; _backRoomFx?.CancelAll(); CancelRaceFetch(); };
            Closed += (_, _) => { lock (Open) Open.Remove(this); DisposeRace(); DisposeDtrh(); if (Spec.Id == FypId) { DisableFypEyes(); CloseFypVisit(); } StopBreakoutAccessWatch(); DisposeBreakoutMedia(); _raceRooms.Clear(); };
            // WPF BackRoom/BackRoomHostService.cs:390,792: Breakout presence lasts until close.
            if (spec.Id == BreakoutDemoId || spec.Id == "breakout")
            {
                Opened += (_, _) => FriendsHead.Service?.EnterActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Breakout);
                Closed += (_, _) => FriendsHead.Service?.LeaveActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Breakout);
            }
            if (spec.Id == PbpId) Opened += (_, _) => OpenPbp();
            if (spec.Id == FypId)
            {
                // WPF FypHostService.cs:720-748 heals a source hidden or minimized by the WM.
                // Here the source IS the ghost: return it to normal rather than leave orphaned buttons.
                PropertyChanged += (_, e) =>
                {
                    if (!_fypGhosted) return;
                    if (e.Property == IsVisibleProperty && !IsVisible) ExitFypGhost();
                    else if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                        Dispatcher.UIThread.Post(() => { if (_fypGhosted && WindowState == WindowState.Minimized) ExitFypGhost(); });
                };
                Closed += (_, _) => ExitFypGhost();
            }
            if (spec.Id == JustDropId) Closed += (_, _) =>
            {
                JustDropOrdersService.Invalidate();
                Windows.MainShellWindow.Current?.RefreshTakeawayShelf();
            };
            if (spec.Id == GlassroadId) Opened += (_, _) => { if (!_raceCard) OpenGlassroad(); };
            if (spec.Id == PrizeCounterId) WireCounter();
        }

        private WebAssetServer? _server;
        private Lazy<GameMediaManifest.Manifest>? _library;   // single-flight: concurrent sit-downs share one walk

        internal void Load(WebAssetServer server, string? room = null)
        {
            if (Spec.Id == JustDropId) { LoadJustDrop(); return; }
            _server = server;
            if (Spec.Id == "loom") DirectoryForLoom();
            if (Spec.Id == PrizeCounterId) StartCounter(FriendsHead.Identity()?.UnifiedId);
            PageUrl = new Uri(server.Url(Spec.Page, RaceQuery(Spec, room)));
            Web.Navigate(PageUrl);
        }

        private readonly Queue<string> _raceRooms = new();
        internal void HandoffRaceRoom(string? room)
        {
            if (_closing || !InviteDestination.IsRoomId(room)) return;
            _raceRooms.Enqueue(room!); FlushRaceRooms();
        }
        private void FlushRaceRooms()
        { if (IsReady) while (_raceRooms.TryDequeue(out var room)) Post(new { type = "join-room", roomId = room }); }

        internal static string? RaceQuery(Game spec, string? room) => spec.Id == GlassroadId && InviteDestination.IsRoomId(room) ? "join=" + room : spec.Query;

        internal static bool SameOrigin(Uri a, Uri b) =>
            a.IsAbsoluteUri && b.IsAbsoluteUri && a.Scheme == b.Scheme && a.Authority == b.Authority;

        /// <summary>WPF ChaosWebViewHost.cs:238,1704 fences transferred documents; JustDropHostService.cs:105-108
        /// keeps site navigation for the sign-in redirect. NativeWebView has no message source;
        /// use its last completed top-level URL, as Intake does. Before the first completion only
        /// host Escape can use an accepted in-flight navigation, not a page business message.</summary>
        private void OnPageMessage(string json)
        {
            if (PageUrl == null) return;
            if (Web.CurrentUrl == null)
            {
                // The listener runs at document start. Before the first completion, accept only
                // host Escape for an allowed Just Drop navigation, never a page business frame.
                if (Spec.Id != JustDropId || !JustDropHandoff.IsSite(Web.ActiveNavigation)) return;
                try
                {
                    var early = JObject.Parse(Unwrap(json));
                    if ((string?)early["type"] == "ccp-host-escape") HandleJustDrop(early);
                }
                catch (Exception ex) { Log.Debug("[Game] unreadable early message: {E}", ex.Message); }
                return;
            }
            if (Spec.Id == JustDropId
                ? !JustDropHandoff.IsSite(Web.CurrentUrl)
                : !ConditioningControlPanel.Services.Quiz.IntakeRun.SameDocument(Web.CurrentUrl, PageUrl)) return;
            HandleMessage(json);
        }

        /// <summary>The shared shell frames (WPF ChaosWebViewHost.OnWebMessage + each host's OnPageReady).</summary>
        internal void HandleMessage(string json)
        {
            JObject o;
            try { o = JObject.Parse(Unwrap(json)); }
            catch (Exception ex) { Log.Debug("[Game] {Id}: unreadable page message: {E}", Spec.Id, ex.Message); return; }
            if (WebHost.HandlePageLog(o, Spec.Id)) return;
            bool handled = false;
            if (Spec.Id == "goon") HandleGoonMessage(o, ref handled);
            if (Spec.Id == "backroom") HandleBackRoomMessage(o, ref handled);
            if (Spec.Id == "arcademy") HandleArcademyMessage(o, ref handled);
            if (handled) return;
            if (Spec.Id == "loom") { HandleLoom(o); return; } // only the Loom bridge subset
            if (Spec.Id == FypId && HandleFyp(o)) return;
            if (Spec.Id == JustDropId) { HandleJustDrop(o); return; }   // the remote site never gets the games' frames
            if (Spec.Id == PrizeCounterId) { HandleCounter(o); return; }   // the counter speaks only its own seam (CONTRACT.md)
            if (Spec.Id == PbpId && HandlePbp(o)) return;
            if (IsRace && HandleRace(o)) return;
            if (IsDtrh && HandleDtrh(o)) return;
            if (IsGlassroad && HandleGlassroad(o)) return;
            switch ((string?)o["type"])
            {
                case "room-option" when IsBreakout:
                    HandleBreakoutOption(o);   // #3921
                    break;
                case "funnel" when IsBreakout:
                    // WPF BackRoomBridge.cs:245-249: the demo end card counts on the one funnel path; after exit
                    // starts, or while minimised, it never opens a checkout (#4066).
                    FunnelPageMessage.Handle(o, !_closing && !_shellExiting && !_roomMinimized && WindowState != WindowState.Minimized);
                    break;
                case "breakout-wall" when IsBreakout:
                    // WPF BackRoomHostService.cs:511 (Sparkles B1): one medal per cleared wall, the Story Demo included.
                    DeliverSparkles(ConditioningControlPanel.Services.Sparkles.Claims.SparklesPageResults.Send(ConditioningControlPanel.Services.Sparkles.Claims.SparklesPageResults.BreakoutWall(o), SparklesHead.Service));
                    break;
                case "ready":
                    IsReady = true;
                    Post(InitMessage());   // WPF BackRoomHostService.BuildInit (:566); no manifest frame for Breakout
                    break;
                case "media-request":
                    // WPF BackRoomBridge.cs:208 -> OnMediaRequestAsync: the station's pictures for this sit-down.
                    // The walk touches every library file, so it runs off the UI thread (WPF OffUi); the reply is posted back.
                    if (IsBreakout) { HandleBreakoutMedia(o); break; }
                    var reqId = (string?)o["reqId"];
                    int count = GameMediaManifest.MediaCount(o["count"]?.Type == JTokenType.Integer ? (int?)o["count"] : null);
                    // The library is walked once per window, like WPF's FlashService snapshot, not on every sit-down.
                    var server = _server;
                    var library = _library ??= new(() => server == null ? new() : GameMediaManifest.BuildLocal(server.AssetUrl));
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        object deal;
                        try { deal = GameMediaManifest.Deal(library.Value, reqId, Random.Shared.Next(), count); }
                        catch (Exception ex) { Log.Debug("[Game] media deal failed: {E}", ex.Message); deal = GameMediaManifest.Deal(new(), reqId, 0, count); }
                        Dispatcher.UIThread.Post(() => Post(deal));
                    });
                    break;
                case "fx-release" when IsBreakout:
                    _backRoomFx?.Release((string?)o["token"] ?? "", (string?)o["station"] ?? "");
                    break;
                case "station-close" when IsBreakout:
                    _backRoomFx?.ReleaseStation((string?)o["station"] ?? "");
                    break;
                case "fx" when IsBreakout:
                    if (!_shellExiting && !_closing && !_roomMinimized) FireRoomFx(o);
                    else Post(new { type = "fx-ack", token = (string?)o["token"], fired = Array.Empty<string>(),
                        skipped = new[] { new { prim = (string?)o["fxId"] ?? "", why = "busy" } } });
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
                case "boot-error":
                    Log.Warning("[Game] {Id}: page boot-error: {Msg}", Spec.Id, (string?)o["msg"]);
                    Close();
                    break;
                case "exit":
                    // The page winds down and answers exit-done; WPF's ArmExitWatchdog closes it anyway.
                    _backRoomFx?.CancelAll();
                    _shellExiting = true;   // on its way out: it no longer keeps an Escape (GameWindow.Escape.cs)
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
        internal object InitMessage()
        {
            var init = new
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
            if (!IsBreakout) return init;
            // WPF BackRoomHostService.cs:589-607: the page must know Off before it starts.
            var frame = JObject.FromObject(init);
            // WPF BackRoomHostService.cs:684-686: the end card reads the current plan list.
            frame["plans"] = ConditioningControlPanel.Services.Vault.PlanCatalog.ToWire(ConditioningControlPanel.Services.Vault.PlanCatalog.Current);
            var motion = CoreSettings.Current.MotionLevel;
            frame["motion"] = motion.ToString().ToLowerInvariant();
            frame["reduced"] = motion != ConditioningControlPanel.Models.MotionLevel.Full;
            var settings = CoreSettings.Current;
            frame["intensity"] = ConditioningControlPanel.Services.BackRoom.BackRoomWire.IntensityWire(settings, motion);
            frame["intensityChoice"] = ConditioningControlPanel.Services.BackRoom.BackRoomWire.IntensityChoiceWire(settings);
            frame["gates"] = JToken.FromObject(ConditioningControlPanel.Services.BackRoom.BackRoomWire.GatesWire(settings));
            frame["media"] = JToken.FromObject(ConditioningControlPanel.Services.BackRoom.BackRoomWire.MediaWire(settings));
            frame["sp"] = settings.SkillPoints;
            frame["invertLook"] = settings.BackRoomInvertLook;
            frame["audio"] = JObject.FromObject(BreakoutAudio());   // WPF BackRoomHostService.cs:589-607 (#3921)
            // WPF BackRoomHostService.cs:32-38,589-609: the br_ keys, resolved in the active language (#3921).
            var loc = LocalizationManager.Instance;
            frame["lex"] = JObject.FromObject(loc.KeysWithPrefix("br_").Distinct(StringComparer.Ordinal).ToDictionary(k => k, loc.Get));
            return frame;
        }

        /// <summary>WPF BreakoutAccess.Current(demo): the demo door is 3 levels, whatever the tier.</summary>
        internal static object? BreakoutEntitlementFor(string id) =>
            id.Equals("breakoutdemo", StringComparison.OrdinalIgnoreCase)
                ? new { storyLimit = 3, endless = false, demo = true }
                : id.Equals("breakout", StringComparison.OrdinalIgnoreCase)
                    ? FullBreakoutEntitlement() : null;

        /// <summary>Every host -&gt; page frame, as sent (tests read the bridge from here).</summary>
        internal event Action<string>? Posted;

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post) through the page's string carrier.</summary>
        internal void Post(object message)
        {
            // Scrolller media: raw urls for a Windows view WPF's gate answers, the loopback relay otherwise (WebHost.ScrolllerFrame).
            var json = Web.ScrolllerFrame(JsonConvert.SerializeObject(message), PageUrl);
            Posted?.Invoke(json);
            if (_closing) return;
            if (RaceShell != null) { RaceShell.Send(json); return; }
            _ = Web.PostJsonAsync(json);
        }

        // ---- panic (PanicSurfaces "games", WPF GameSurfaces.cs:53-55 -> CloseActive("panic")) ----

        internal static bool IsAnyOpen() { lock (Open) return Open.Count > 0; }
        internal static bool IsAnyStartupGameOpen() { lock (Open) return Open.Any(w => w.Spec.Id != FypId && w.Spec.Id != "loom"); }

        internal static GameWindow[] OpenWindowsForTests() { lock (Open) return Open.ToArray(); }

        /// <summary>The open game window that is the active window, if any (friends landing anchor).</summary>
        internal static GameWindow? ActiveOpen() { lock (Open) return Open.FirstOrDefault(w => w.IsActive); }

        internal static GameWindow? OpenForTests(string id) => OpenWindowsForTests().FirstOrDefault(w => w.Spec.Id == id);

        internal static void CloseAllForPanic()
        {
            _dtrhPanicEpoch++; // also cancels a recovery whose Closed callback re-enters panic
            GameWindow[] all;
            lock (Open) all = Open.ToArray();
            foreach (var w in all)
            {
                try { if (w.IsDtrh) w.CloseDtrhForPanic(); else w.CloseFor(PrizeCounterRules.ClosePanic); }
                catch (Exception ex) { Log.Warning(ex, "[Game] panic close of {Id} failed", w.Spec.Id); }
            }
        }
    }
}
