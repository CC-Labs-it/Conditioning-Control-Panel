// The Goon Game on this head, part A (WPF ConditioningControlPanel/Services/GoonGame/GoonHostService.cs).
// Ported: the Play door registers the page (StartUrl :290), the boot handshake answers ready with init and
// manifest (OnPageReady :426-545), the page-driven window mode is applied, echoed and remembered
// (ApplyHostFullscreen :983, StartFullscreen :300), presence lasts while the window is open (:328), the
// window keeps Escape as its pause (MainWindow.xaml.cs:1526-1588) and the heartbeat watchdog (:1667-1782).
// Practice runs on the page's own loopback. Panic closes it with every other game (PanicSurfaces "games";
// WPF GameSurfaces.cs:64).
// Part B: the net-post proxy (:1008) is answered as unreachable (status 0, the page's offline path) until a
// trusted sender-origin seam exists, so hosted and joined duels do not connect yet; toys (:749), Sparkles
// match-result (:757), cache and inbox verbs (:776-785), Discord verbs (:789-804), online media (:807-819),
// share card (:835), main-window duck (:385) and the boot-error dialog (:1801).
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal const string GoonId = "goon";

    /// <summary>WPF GoonHostService.cs:66 ProxyBaseUrl, sent so the page reads the same net block.</summary>
    private const string GoonProxyBase = "https://codebambi-proxy.vercel.app";

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
    }

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
        _goonWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GoonHostRules.WatchIntervalSeconds) };
        _goonWatch.Tick += (_, _) => CheckGoonWatch(GoonUtcNow());
        _goonWatch.Start();
    }

    partial void HandleGoonMessage(JObject message, ref bool handled)
    {
        switch ((string?)message["type"])
        {
            case "ready":
                _goonBeat = _goonPaintMove = _goonTick = GoonUtcNow();
                _goonPaint = null;
                IsReady = true;
                try { Web.Focus(); } catch (Exception ex) { Log.Debug("[Game] goon focus: {E}", ex.Message); }
                Post(GoonInitMessage());
                var m = _server == null ? new GameMediaManifest.Manifest() : GameMediaManifest.BuildActive(WebAssetServer.AssetPath);
                var manifest = JObject.FromObject(GameMediaManifest.Frame(m));
                manifest["received"] = new JArray();   // WPF :519: the ephemeral inbox, empty here
                Post(manifest);
                Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                handled = true;
                break;
            case "fullscreen-set":
                SetGoonFullscreen(message["on"]?.Type == JTokenType.Boolean && (bool)message["on"]!);
                handled = true;
                break;
            case "net-post":
                // Part B (WPF :1008). Status 0 is the page's "never got an answer" path, so it shows
                // offline at once instead of waiting out its 45 s timeout. No request leaves the app.
                Post(new { type = "net-post-result", id = (string?)message["id"] ?? "", status = 0, body = "" });
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

    internal void CheckGoonWatch(DateTime now)
    {
        var gap = (now - _goonTick).TotalSeconds;
        _goonTick = now;
        if (_closing || _shellExiting || !IsReady) return;
        var silent = (now - _goonBeat).TotalSeconds;
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

    private void StopGoonWatch()
    {
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

    /// <summary>WPF OnPageReady (:443-504) with the fields this head can honour: no toy director
    /// (haptics false), no Discord or online-media block (the page reads null as none).</summary>
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
            // The token stays in the app: no net-post proxy on this head yet (part B).
            net = new { serverBase = GoonProxyBase, authToken = "", viaHost = true },
            caps = new
            {
                haptics = false,
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
            plans = ConditioningControlPanel.Services.Vault.PlanCatalog.ToWire(ConditioningControlPanel.Services.Vault.PlanCatalog.Current),
        };
    }
}
