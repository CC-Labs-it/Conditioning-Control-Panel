// The Goon Game on this head, part A (WPF ConditioningControlPanel/Services/GoonGame/GoonHostService.cs).
// Ported: the Play door registers the page (StartUrl :290), the boot handshake answers ready with init and
// manifest (OnPageReady :426-545), the page-driven window mode is applied, echoed and remembered
// (ApplyHostFullscreen :983, StartFullscreen :300), presence lasts while the window is open (:328), the
// window keeps Escape as its pause (MainWindow.xaml.cs:1526-1588), the heartbeat watchdog (:1667-1782), toy cues on
// a connected device (:749-756, HookToys :2074-2099) and the practice Sparkles claim with its slip (:757-768).
// Practice runs on the page's own loopback. Panic closes it with every other game (PanicSurfaces "games";
// WPF GameSurfaces.cs:64).
// The net-post proxy (:1008-1087) relays /v2/goon/ only, and only for the navigation-confirmed page; stake
// frames reach the StakeBridge; cache URLs point at this head's token-gated loopback.
// Not ported: the last-opponent record (:767), Discord verbs (:789-804), main-window duck (:385) and the
// boot-error dialog (:1801).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Stakes;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal const string GoonId = "goon";

    /// <summary>WPF GoonHostService.cs:66 ProxyBaseUrl, sent so the page reads the same net block.</summary>
    private const string GoonProxyBase = GoonSignalingClient.ProxyBaseUrl;

    // WPF GoonHostService.cs:210: one client, a 40 s ceiling for the page's long-polls.
    private static readonly Lazy<HttpClient> GoonHttp = new(() =>
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(40) };
        try { client.DefaultRequestHeaders.UserAgent.ParseAdd("ConditioningControlPanel/" + CoreReleaseContent.AppVersion); }
        catch (Exception ex) { Diag.Swallowed(ex); }
        return client;
    });
    internal static Func<HttpClient> GoonHttpClient = () => GoonHttp.Value;
    private CancellationTokenSource? _goonNet;   // in-flight relays; cancelled on close (panic closes too)

    private static StakeBridge? _goonStakes;
    private static Func<string, Action<JObject>, StakeBridge> _goonStakeBridge = StakeBridge.ForApp;
    /// <summary>Test seam, as <see cref="PbpStakeBridge"/>: setting it drops the cached bridge.</summary>
    internal static Func<string, Action<JObject>, StakeBridge> GoonStakeBridge
    {
        get => _goonStakeBridge;
        set { _goonStakeBridge = value; _goonStakes = null; }
    }
    internal static Task GoonStakePending { get; private set; } = Task.CompletedTask;

    /// <summary>WPF PostStake: a stake frame to the open Goon page; a closed one is a quiet no-op.</summary>
    private static void PostGoonStake(JObject frame) => Dispatcher.UIThread.Post(() =>
    {
        GameWindow? w;
        lock (Open) w = Open.FirstOrDefault(x => x.Spec.Id == GoonId && !x._closing);
        w?.Post(frame);
    });

    // WPF GoonHostService.cs:290: https://ccp.game/goon/index.html. The sign-in door runs first
    // (MainShellWindow.LaunchPlayGoon; WPF MainWindow.PlayTab.cs:60-64); Launch itself has no gate.
    static partial void RegisterGoon(Dictionary<string, Game> games) =>
        games[GoonId] = new(GoonId, "launcher_game_goon_title", "goon/index.html");

    /// <summary>WPF :300,328: built in the remembered window mode; presence until close.</summary>
    private void WireGoon()
    {
        // WPF :300,1789-1792: a recovery relaunch comes back windowed, so a re-wedged page keeps a close button.
        if (CoreSettings.Current.GoonFullscreen && !_goonRecoveryWindowed) WindowState = WindowState.FullScreen;
        Opened += (_, _) => FriendsHead.Service?.EnterActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Goon);
        Closed += (_, _) => FriendsHead.Service?.LeaveActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Goon);
        // WPF :361,2110 UnhookToys: the toy first, and refused until a window is up again (panic included).
        Closing += (_, _) =>
        {
            _goonNet?.Cancel();
            GoonToyCues.Close();
            if (_goonToys != null) _goonToys.ConnectionChanged -= OnGoonToys;
            _goonToys = null;
        };
    }

    // WPF HookToys (:2074): cues may play while the window is up, and the page hears a toy connect or drop.
    private HapticService? _goonToys;
    private void HookGoonToys()
    {
        GoonToyCues.Open();
        if (_goonToys != null || CoreHaptics.Service is not { } haptics) return;
        haptics.ConnectionChanged += OnGoonToys;
        _goonToys = haptics;
    }

    private void OnGoonToys(object? sender, bool connected) => Dispatcher.UIThread.Post(() =>
    {
        if (!_closing) Post(new { type = "haptics-state", connected = GoonToyCues.ReadyNow });
    });

    // WPF GoonHostService.cs:1667-1782: a live page must keep beating and painting.
    internal static Func<DateTime> GoonUtcNow = () => DateTime.UtcNow;
    private DispatcherTimer? _goonWatch;
    private DateTime _goonBeat, _goonPaintMove, _goonTick;
    private long? _goonPaint;
    private bool _goonRetried;
    private static int _goonPanicEpoch;
    private static bool _goonRecoveryWindowed;

    private void StartGoonWatch()
    {
        _goonBeat = _goonPaintMove = _goonTick = GoonUtcNow();
        GoonCacheBridge.Attach(PostGoonCache);
        _goonWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GoonHostRules.WatchIntervalSeconds) };
        _goonWatch.Tick += (_, _) => CheckGoonWatch(GoonUtcNow());
        _goonWatch.Start();
    }

    partial void HandleGoonMessage(JObject message, ref bool handled)
    {
        if (HandleGoonMedia(message) || HandleGoonInbox(message)) { handled = true; return; }
        if (StakeBridge.Handles((string?)message["type"]))
        {
            if (!_closing) { _goonStakes ??= GoonStakeBridge(GoonId, PostGoonStake); GoonStakePending = _goonStakes.Handle(message); }
            handled = true;
            return;
        }
        switch ((string?)message["type"])
        {
            case "cache-req":
            case "cache-put":
            case "encode-done":
                if (!_closing) GoonCacheBridge.OnMessage(message);
                handled = true;
                break;
            case "ready":
                BootGoonInbox();
                _goonBeat = _goonPaintMove = _goonTick = GoonUtcNow();
                _goonPaint = null;
                IsReady = true;
                HookGoonToys();
                try { Web.Focus(); } catch (Exception ex) { Log.Debug("[Game] goon focus: {E}", ex.Message); }
                Post(GoonInitMessage());
                Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                handled = true;
                break;
            case "fullscreen-set":
                SetGoonFullscreen(message["on"]?.Type == JTokenType.Boolean && (bool)message["on"]!);
                handled = true;
                break;
            case "toy-pattern":
                // WPF :749: clamped to the cap the page sends; the player's master cap applies in the mixer.
                GoonToyCues.OnPattern(message);
                handled = true;
                break;
            case "toy-stop":
                GoonToyCues.Stop();
                handled = true;
                break;
            case "match-result":
                // WPF :757-768: practice claims practice, a leave or mercy claims nothing; the slip goes to the page.
                DeliverSparkles(ConditioningControlPanel.Services.Sparkles.Claims.SparklesPageResults.Send(
                    ConditioningControlPanel.Services.Sparkles.Claims.SparklesPageResults.Goon(message), SparklesHead.Service));
                handled = true;
                break;
            case "open-prime":
                // WPF :813: the Prime sheet and recap send the player to the app's own refusal and upgrade path.
                TierGate.DemandPremium(Loc.Get("launcher_game_goon_title"));
                handled = true;
                break;
            case "net-post":
                if (!_closing) RelayGoonPost(message);
                handled = true;
                break;
            case "share-card":
                if (!_closing) GoonSharePending = ShareGoonCard(message);
                handled = true;
                break;
            case "heartbeat":
            case "pong":
                var now = GoonUtcNow();
                _goonBeat = now;
                var vis = (string?)message["vis"];
                if (!string.IsNullOrEmpty(vis) && vis != "visible") _goonPaintMove = now;
                else if (message["paint"]?.Type == JTokenType.Integer && long.TryParse(message["paint"]!.ToString(), out var paint) && paint != _goonPaint)
                {
                    _goonPaint = paint;
                    _goonPaintMove = now;
                }
                handled = true;
                break;
        }
    }

    // WPF GoonHostService.cs:1008-1087. The account token rides only to the proxy's /v2/goon/ routes, and
    // only for a ready page on a navigation-confirmed Goon document (OnPageMessage already drops any
    // other document). A reply reaches only the document that asked; close and panic cancel the request.
    private void RelayGoonPost(JObject message)
    {
        if (!IsReady || !_goonNavigationConfirmed) return;
        var id = (string?)message["id"] ?? "";
        var generation = Volatile.Read(ref _goonInboxGeneration);
        void Reply(int status, string body) => Dispatcher.UIThread.Post(() =>
        {
            if (!_closing && generation == Volatile.Read(ref _goonInboxGeneration))
                Post(new { type = "net-post-result", id, status, body });
        });
        if (!HostNetPathRule.TryBuild(GoonSignalingClient.ProxyBaseUrl, (string?)message["path"], "/v2/goon/", out var target) || target == null)
        {
            Reply(0, "forbidden_path");
            return;
        }
        var body = message["body"]?.Type == JTokenType.String ? (string?)message["body"] ?? ""
            : message["body"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
        var token = PbpAuthToken();
        var ct = (_goonNet ??= new CancellationTokenSource()).Token;
        _ = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                if (token.Length > 0) request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                using var response = await GoonHttpClient().SendAsync(request, ct).ConfigureAwait(false);
                var result = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                MergedAccountRecovery.TryHandle((int)response.StatusCode, result);   // contract D
                Reply((int)response.StatusCode, result);
            }
            catch (Exception ex) { Log.Debug("[Goon] net-post failed: {E}", ex.Message); Reply(0, ""); }
        }, ct);
    }

    internal void CheckGoonWatch(DateTime now)
    {
        var gap = (now - _goonTick).TotalSeconds;
        _goonTick = now;
        if (_closing || _shellExiting || !IsReady) return;
        var silent = (now - _goonBeat).TotalSeconds;
        // WPF :1716-1718: a page that stopped beating cannot send toy-stop, so its cue ends long before recovery.
        if (silent > 6) GoonToyCues.Stop();
        if (silent > 20) { RecoverGoon(); return; }
        if (_goonPaint != null)
        {
            var verdict = GoonHostRules.PaintStallVerdict((now - _goonPaintMove).TotalSeconds, gap, 0);
            if (verdict == GoonHostRules.PaintStallCall.UiStalled) _goonPaintMove = now;
            else if (verdict == GoonHostRules.PaintStallCall.Stalled) { RecoverGoon(); return; }
        }
        if (silent > 8) Post(new { type = "ping" });
    }

    private void RecoverGoon()
    {
        var retry = !_goonRetried;
        var epoch = _goonPanicEpoch;
        Close();
        if (!retry || epoch != _goonPanicEpoch) return;
        GameWindow? next;
        _goonRecoveryWindowed = true;
        try { next = LaunchWindow(Spec); }
        finally { _goonRecoveryWindowed = false; }
        if (next != null) _goonRetried = next._goonRetried = true;
    }

    // WPF's ccp.assets and ccp.cache virtual hosts become this window's token-gated loopback URLs.
    internal void PostGoonCache(object frame) => Dispatcher.UIThread.Post(() =>
    {
        if (_closing || _server == null) return;
        var json = JObject.FromObject(frame);
        foreach (var value in json.Descendants().OfType<JValue>())
        {
            if (value.Value is not string url) continue;
            if (url.StartsWith("https://ccp.assets/", StringComparison.Ordinal))
                value.Value = _server.Url(WebAssetServer.AssetsPrefix + url["https://ccp.assets/".Length..]);
            else if (url.StartsWith("https://ccp.cache/", StringComparison.Ordinal))
                value.Value = _server.Url("__goon_cache__/" + url["https://ccp.cache/".Length..]);
        }
        Post(json);
    });

    private void StopGoonWatch()
    {
        StopGoonMedia();
        // WPF GoonHostService.cs:2113-2118: a closed match must not strand the queue.
        try { GoonCacheBridge.Detach(); } catch (Exception ex) { Diag.Swallowed(ex); }
        try { TransferCompressionService.Instance.ResumeAfterMatch(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _goonWatch?.Stop();
        _goonWatch = null;
    }

    /// <summary>WPF ApplyHostFullscreen (:983): apply, echo the real state, remember it.</summary>
    internal void SetGoonFullscreen(bool on)
    {
        if (_closing) return;
        WindowState = on ? WindowState.FullScreen : WindowState.Normal;
        bool now = WindowState == WindowState.FullScreen;
        Post(new { type = "fullscreen", on = now });
        if (CoreSettings.Current.GoonFullscreen == now) return;
        CoreSettings.Current.GoonFullscreen = now;
        CoreSettings.Service?.Save();
    }

    /// <summary>WPF OnPageReady (:443-504) with the fields this head can honour: no Discord block.</summary>
    internal object GoonInitMessage()
    {
        var settings = CoreSettings.Current;
        var consent = new ConsentSheetMsg();
        return new
        {
            type = "init",
            protocol = GoonConsts.ProtocolVersion,
            lang = settings.Language ?? "en",
            solo = !CoreAccount.IsLoggedIn,
            identity = new
            {
                unifiedId = CoreAccount.UnifiedUserId ?? "",
                displayName = string.IsNullOrWhiteSpace(settings.UserDisplayName) ? "Player" : settings.UserDisplayName,
                appVersion = CoreReleaseContent.AppVersion,
            },
            // WPF :463: the page needs the token for its own requests; only a navigation-confirmed Goon document gets it.
            net = new { serverBase = GoonProxyBase, authToken = _goonNavigationConfirmed ? PbpAuthToken() : "", viaHost = true },
            caps = new
            {
                haptics = GoonToyCues.ReadyNow,   // WPF :469
                brainDrain = true,
                spiral = true,
                camera = false,
                video = true,
                mediaTransfer = GoonHostRules.TransferAllowed(),
                canHost = GoonHostRules.HostingAllowed(),
                canJoin = GoonHostRules.JoiningAllowed(),
            },
            joinCode = "",
            autoHost = false,
            consent = new { liveDurationSec = consent.LiveDurationSec, toyCap = consent.ToyCap, payloadMinGapMs = consent.PayloadMinGapMs },
            fullscreen = WindowState == WindowState.FullScreen,
            media = GoonMediaBlock(),
            plans = ConditioningControlPanel.Services.Vault.PlanCatalog.ToWire(ConditioningControlPanel.Services.Vault.PlanCatalog.Current),
        };
    }
}
