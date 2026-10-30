using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Safety;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

/// <summary>
/// Host for GLASSROAD (<c>Resources/web/glassroad/index.html</c>), Racing Thoughts 3D, PRIME ONLY
/// (tier 2, solo included; owner 2026-10-29: "THE whole feature is for primes"). Every race door
/// reaches <see cref="Launch"/>, which asks <see cref="DemandPrime"/> first. A sibling of <see cref="PieceByPieceHostService"/>: one
/// windowed <see cref="ChaosWebViewHost"/> (it attaches ScrolllerWebGate), own profile, host
/// protocol v2 (host/bridge.js). INIT GOES OUT BEFORE READY: the page awaits init at boot (3 s,
/// then defaults with no net) but says ready only once shaders are warm, and Post queues until
/// ready, so init goes straight to the core at DOMContentLoaded, on the first message and on ready
/// (a repeat is a no-op). The relay signs as the player, so it forwards ONLY <c>/v2/race/</c>,
/// judged on the built Uri (<see cref="HostNetPathRule"/>). Every exit is <see cref="Close"/>.
/// </summary>
internal static class GlassroadHostService
{
    public const string ProductName = "Racing Thoughts";
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    private static readonly TimeSpan BootDeadline = TimeSpan.FromSeconds(45);

    /// <summary>The page's kept-Escape frame (host/hostEscape.js HOST_ESCAPE).</summary>
    internal const string KeptEscapeType = "kept-escape";

    private static readonly HttpClient Http = BuildHttpClient();

    private static HttpClient BuildHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}"); }
        catch (Exception ex) { Diag.Swallowed(ex); }
        return c;
    }

    private static ChaosWebViewHost? _host;
    private static CoreWebView2? _core;
    private static DispatcherTimer? _watch;
    private static DateTime _lastHeartbeatUtc, _lastProgressUtc;
    private static bool _pinged, _earlyInitSent, _disposing;

    public static bool IsActive => _host != null;

    /// <summary>The Prime bar (tier 2, whitelist and grace folded in), the same TierGate read every
    /// other Prime door makes. Fails closed: signed out or no Patreon service reads as locked.</summary>
    public static bool PrimeAllowed => TierGate.RequiresLab(FeatureName).Allowed;

    /// <summary>Gate-and-tell: true to open, false after the Prime refusal toast (See tiers).</summary>
    internal static bool DemandPrime() => TierGate.DemandLab(FeatureName);

    private static string FeatureName => Localization.Loc.Get("launcher_game_race_title");

    /// <summary>The window is in front: the panel lets the page keep a first Escape as its pause.</summary>
    public static bool IsInFront => _host?.IsForeground == true;

    public static bool IsReady => _host?.IsReady == true;

    /// <summary>Hand the page an Escape the panel kept as its pause (pause, never leave).</summary>
    public static void PostKeptEscape()
    {
        try { _host?.Post(new { type = KeptEscapeType }); _host?.FocusWeb(); }
        catch (Exception ex) { App.Logger?.Debug("Glassroad: kept Escape post failed: {E}", ex.Message); }
    }

    /// <summary>Open the race window. Idempotent: a live window is only re-focused. With a room id
    /// (a Lobby row, a friend's race invite) the page opens on <c>?join=</c> and seats us; a live
    /// window gets the id as a <c>join-room</c> frame, which the page takes only on its menu.</summary>
    public static void Launch(string? joinRoomId = null)
    {
        // Prime only: below tier 2 no door opens the race; the refusal is the app's Prime toast.
        if (!DemandPrime()) return;
        var join = Lobby.RaceJoinLink.For(joinRoomId);
        if (_host != null)
        {
            if (join != null) _host.Post(new { type = "join-room", roomId = joinRoomId });
            _host.FocusWeb();
            return;
        }
        try
        {
            _pinged = _earlyInitSent = false;
            _lastProgressUtc = _lastHeartbeatUtc = DateTime.UtcNow;
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = join?.AbsoluteUri ?? Lobby.RaceJoinLink.Base,
                PrimaryHost = "ccp.game",
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    // The page, three.js and the Arcademy Loom it imports; never read cross-origin.
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    // Local media copies, drawn into canvases: Allow, as every host.
                    ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                },
                // Own profile; the argument string is a constant, as WebView2 needs per folder.
                UserDataFolderName = "browser_data_glassroad",
                InputEnabled = true,
                IsGame = true,
                StartFullscreen = false,
                OwnedByMainWindow = true,
                WindowTitle = ProductName,
                LogTag = "Glassroad",
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnCoreCreated = core => { _core = core; core.DOMContentLoaded += OnDomContentLoaded; },
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = kind =>
                {
                    App.Logger?.Warning("Glassroad: browser process failed ({Kind}) - closing", kind);
                    RunOnUi(Close);
                },
            });
            _host.Show();
            if (_host.Window != null) _host.Window.Closed += (_, _) => Close();
            StartWatch();
            if (App.Ownership != null) { App.Ownership.OwnershipChanged -= OnOwnershipChanged; App.Ownership.OwnershipChanged += OnOwnershipChanged; }
            _host.FocusWeb();
            try { App.Friends?.EnterActivity(Friends.PresenceActivity.Race); } catch (Exception ex) { Diag.Swallowed(ex); }
            // Feature day log: one e_race3d per window opened (re-focusing a live window is not a launch).
            try { SeasonRecapService.TrackFeature(Models.SeasonFeatureKeys.Race3d); } catch (Exception ex) { Diag.Swallowed(ex); }
            App.Logger?.Information("GlassroadHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "GlassroadHostService.Launch failed");
            Close();
        }
    }

    /// <summary>The one funnel for every exit (page, title-bar X, panic, boot failure, dead render
    /// process, watchdog, shutdown). Idempotent and direct.</summary>
    public static void Close()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            try { _watch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _watch = null;
            bool had = _host != null;
            try { if (_core != null) _core.DOMContentLoaded -= OnDomContentLoaded; } catch (Exception ex) { Diag.Swallowed(ex); }
            _core = null;
            try { if (App.Ownership != null) App.Ownership.OwnershipChanged -= OnOwnershipChanged; } catch (Exception ex) { Diag.Swallowed(ex); }
            _counterOpen = false;
            try { _host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _host = null;
            if (had)
            {
                try { App.Friends?.LeaveActivity(Friends.PresenceActivity.Race); } catch (Exception ex) { Diag.Swallowed(ex); }
                App.Logger?.Information("GlassroadHostService: closed");
            }
        }
        catch (Exception ex) { App.Logger?.Debug("GlassroadHostService.Close: {E}", ex.Message); }
        finally { _disposing = false; }
    }

    // ============================ init ============================

    private static void OnDomContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e) => PostInitNow();

    /// <summary>Straight to the core, past the ready queue (see the class remarks).</summary>
    private static void PostInitNow()
    {
        try
        {
            var s = App.Settings?.Current;
            var uid = App.UnifiedUserId;
            bool online = !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(SafeAuthToken());
            int tier = 0;
            try { tier = EntitlementTierSync.EffectiveTierNow(); } catch (Exception ex) { Diag.Swallowed(ex); }
            var init = GlassroadRules.InitFrame(online ? uid : null, s?.UserDisplayName, tier, GentleVisuals.On,
                s?.GentleFadeSeconds ?? GentleVisuals.DefaultFadeSeconds, s?.GentleSpeed ?? GentleVisuals.DefaultSpeed,
                App.Ownership?.Grants);
            _core?.PostWebMessageAsJson(init.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { App.Logger?.Debug("Glassroad: init post failed: {E}", ex.Message); }
    }

    /// <summary>A Prize Counter buy (or any new snapshot) while the race is up: the garage lifts the
    /// kart bodies it now owns (host -> page <c>owned</c>, glassroad CONTRACT.md section 7).</summary>
    private static void OnOwnershipChanged(object? sender, Prizes.OwnershipChangedEventArgs e)
    {
        try { _host?.Post(GlassroadRules.OwnedFrame(App.Ownership?.Grants)); }
        catch (Exception ex) { App.Logger?.Debug("Glassroad: owned post failed: {E}", ex.Message); }
    }

    private static void OnPageReady()
    {
        _lastHeartbeatUtc = _lastProgressUtc = DateTime.UtcNow;
        _pinged = false;
        PostInitNow();
        _host?.FocusWeb();
    }

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        _lastProgressUtc = DateTime.UtcNow;
        try
        {
            if (!_earlyInitSent && _host is { IsReady: false })
            {
                _earlyInitSent = true;
                PostInitNow();   // backstop for a DOMContentLoaded that beat the bridge
            }
            switch ((string?)o["type"])
            {
                case "heartbeat":
                case "pong":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    _pinged = false;
                    break;
                case "net":
                    OnNetRequest(o);
                    break;
                case "friends-list":
                    // The pre-race room's invite view (host/friendInvite.js): online friends, uid + name only.
                    _host?.Post(new { type = "friends-list-reply", id = (string?)o["id"] ?? "",
                        friends = GlassroadRules.FriendRows(App.Friends?.Snapshot.Friends) });
                    break;
                case "invite-friend":
                    _ = OnInviteFriendAsync(o);
                    break;
                case "race-result":
                    OnRaceResult(o);
                    break;
                case "open-prize-counter":
                    RunOnUi(OpenPrizeCounter);
                    break;
                case "exit":
                case "menu-back":
                    // The page's own Leave / the menu's Back link. Escape never sends these.
                    RunOnUi(Close);
                    break;
                case "boot-error":
                    App.Logger?.Warning("Glassroad: page boot-error: {Msg}", (string?)o["message"] ?? (string?)o["msg"]);
                    RunOnUi(Close);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Glassroad: message handler threw ({Type}): {E}", (string?)o["type"], ex.Message);
        }
    }

    /// <summary>The Prize Counter is open over the garage (one close hook at a time).</summary>
    private static bool _counterOpen;

    /// <summary>The garage's Prize Counter row (7.2 contract 1, owner call: no per-game relay): pause the page and
    /// open the counter WINDOW over it on the kart tab (the shared seam, <c>Launch(from, tab, onClosed)</c>). When
    /// the counter closes the page gets the owned frame again, so a look bought there lights up even if no
    /// ownership event reached us.</summary>
    private static void OpenPrizeCounter()
    {
        if (_host == null) return;
        try { _host.Post(new { type = "pause", on = true }); } catch (Exception ex) { Diag.Swallowed(ex); }
        if (_counterOpen) { PrizeCounter.PrizeCounterHostService.BringForward(); return; }
        _counterOpen = true;
        PrizeCounter.PrizeCounterHostService.Launch(GlassroadRules.CounterFrom, GlassroadRules.CounterTab,
            reason => RunOnUi(() => OnCounterClosed(reason)));
    }

    /// <summary>The counter went: a plain back re-sends <c>owned</c> and hands the keyboard back; panic and app
    /// exit close this window too, so nothing comes forward then.</summary>
    private static void OnCounterClosed(string reason)
    {
        if (!_counterOpen) return;
        _counterOpen = false;
        var worn = reason == PrizeCounter.PrizeCounterHostService.CloseWear ? PrizeCounter.PrizeCounterHostService.WornItem : null;
        var (resend, focus) = GlassroadRules.CounterReturn(reason, kartWorn: worn != null);
        try
        {
            if (resend) _host?.Post(GlassroadRules.OwnedFrame(App.Ownership?.Grants, worn));
            if (focus) _host?.FocusWeb();
        }
        catch (Exception ex) { App.Logger?.Debug("Glassroad: owned re-send after the counter failed: {E}", ex.Message); }
    }

    /// <summary>SPARKLES BY PLACE (B1): a finished race claims; the server prices it, tapers repeats
    /// and, online, reads the room's signed result instead of ours. Fire and forget.</summary>
    private static void OnRaceResult(JObject o)
    {
        var claim = GlassroadRules.SparklesClaimFor(o);
        if (claim == null)
        {
            App.Logger?.Debug("Glassroad: race-result earns no Sparkles claim (mode {Mode})", (string?)o["mode"]);
            return;
        }
        // The results slip (W7) lands on the results card that is up when the receipt comes in.
        var slipHost = _host;
        _ = Sparkles.SparklesSlip.Deliver(Sparkles.SparklesGameEnd.Claim(GlassroadRules.SparklesGame, claim.Mode,
            claim.Result, claim.Duration, claim.Detail, claim.Proof), json => slipHost?.Post(new JRaw(json)));
    }

    /// <summary><c>net { id, method, path, body }</c> -&gt; <c>net-reply { id, status, body }</c>. A
    /// refusal or failure answers status 0 (the page's "no answer"); no id goes unanswered.</summary>
    private static void OnNetRequest(JObject o)
    {
        var id = (string?)o["id"] ?? "";
        var path = (string?)o["path"] ?? "";
        var method = ((string?)o["method"] ?? "GET").Trim().ToUpperInvariant();
        var b = o["body"];
        var body = b == null || b.Type == JTokenType.Null ? ""
            : b.Type == JTokenType.String ? (string?)b ?? "" : b.ToString(Newtonsoft.Json.Formatting.None);

        if (method != "GET" && method != "POST") { ReplyNet(id, 0, "forbidden_method"); return; }
        // Signed first (the host names the caller), then the SIGNED path is judged on its built Uri.
        if (!GlassroadRules.TrySign(method, path, body, App.UnifiedUserId, out var signedPath, out var signedBody)
            || !HostNetPathRule.TryBuild(ProxyBaseUrl, signedPath, GlassroadRules.AllowedPathPrefix, out var target)
            || target is null)
        {
            App.Logger?.Warning("Glassroad: net REJECTED for path '{Path}'", path);
            ReplyNet(id, 0, "forbidden_path");
            return;
        }

        _ = Task.Run(async () =>
        {
            int status = 0;
            string reply = "";
            try
            {
                using var request = PbpNetRelay.Build(target, method, signedPath, signedBody,
                    SafeAuthToken(), UpdateService.AppVersion);
                using var response = await Http.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                status = (int)response.StatusCode;
                reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                MergedAccountRecovery.TryHandle(status, reply);
            }
            catch (Exception ex)
            {
                status = 0;
                reply = "";
                App.Logger?.Debug("Glassroad: net {Method} {Path} failed: {E}", method, path, ex.Message);
            }
            ReplyNet(id, status, reply);
        });
    }

    /// <summary><c>invite-friend { id, friendUid, roomId }</c> -&gt; <c>invite-friend-reply { id, sent, note }</c>.
    /// Only a friend on this account's list and a well-formed room id leave; the server checks the room
    /// (host only, open until the race starts) and answers a fixed reason, worded here in the player's language.</summary>
    private static async Task OnInviteFriendAsync(JObject o)
    {
        var id = (string?)o["id"] ?? "";
        var uid = (string?)o["friendUid"];
        var room = (string?)o["roomId"];
        var outcome = new SendOutcome(SendResult.Refused, null);
        try
        {
            var svc = App.Friends;
            bool known = svc != null && !string.IsNullOrEmpty(uid)
                && System.Linq.Enumerable.Any(svc.Snapshot.Friends, f => f.Id == uid);
            if (svc != null && known && InviteDestination.IsRoomId(room))
                outcome = await svc.InviteToRaceAsync(uid!, room!);
        }
        catch (Exception ex)
        {
            outcome = new SendOutcome(SendResult.TryLater, null);
            App.Logger?.Debug("Glassroad: race invite failed: {E}", ex.Message);
        }
        bool sent = Controls.Friends.FriendsDrawerRules.IsGood(outcome.Result);
        var note = GlassroadRules.Sentence(Localization.Loc.Get(GlassroadRules.InviteNoteKey(outcome)));
        App.Logger?.Information("Glassroad: race invite {Result} {Reason}", outcome.Result, outcome.Reason ?? "");
        RunOnUi(() =>
        {
            try { _host?.Post(new { type = "invite-friend-reply", id, sent, note }); }
            catch (Exception ex) { App.Logger?.Debug("Glassroad.ReplyInvite: {E}", ex.Message); }
        });
    }

    private static void ReplyNet(string id, int status, string body) => RunOnUi(() =>
    {
        try { _host?.Post(new { type = "net-reply", id, status, body }); }
        catch (Exception ex) { App.Logger?.Debug("Glassroad.ReplyNet: {E}", ex.Message); }
    });

    private static string SafeAuthToken()
    {
        try { return App.Settings?.Current?.AuthToken ?? string.Empty; }
        catch { return string.Empty; }
    }

    // ============================ plumbing ============================

    private static void RunOnUi(Action act)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { try { act(); } catch (Exception ex) { Diag.Swallowed(ex); } return; }
        if (disp.HasShutdownStarted) return;
        disp.BeginInvoke(act, DispatcherPriority.Normal);
    }

    /// <summary>One 5 s watchdog. Before ready: close after 45 s with no page message at all (the
    /// bridge beats every 2 s, so only a page that never ran fails). After ready: silent past 20 s
    /// gets one ping, then the window closes on the next tick.</summary>
    private static void StartWatch()
    {
        _watch = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
        _watch.Tick += (_, _) =>
        {
            if (_host == null) return;
            var now = DateTime.UtcNow;
            if (!_host.IsReady)
            {
                if (now - _lastProgressUtc < BootDeadline) return;
                App.Logger?.Warning("Glassroad: no page progress for {S}s - closing", BootDeadline.TotalSeconds);
                Close();
                return;
            }
            if ((now - _lastHeartbeatUtc).TotalSeconds <= 20) return;
            if (!_pinged)
            {
                _pinged = true;
                try { _host.Post(new { type = "ping", t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }); } catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }
            App.Logger?.Warning("Glassroad: page heartbeat silent >20s and no pong - closing");
            Close();
        };
        _watch.Start();
    }
}
