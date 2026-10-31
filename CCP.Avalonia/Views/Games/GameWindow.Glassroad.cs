// PORTED from ConditioningControlPanel/Services/Glassroad/GlassroadHostService.cs (feat/glassroad-a 5121933ee):
// GLASSROAD, the 3D race (Assets/web/glassroad/index.html), private preview behind CCP_GLASSROAD=1. With the switch
// on, the Racing Thoughts door opens this page instead (GameWindow.Launch); without it nothing changes for anyone.
// INIT GOES OUT BEFORE READY: the page awaits init at boot (3 s, then defaults with no net) but says ready only once
// shaders are warm, so init is posted at the page's load (NavigationCompleted, this engine's nearest seam to WPF's
// DOMContentLoaded), on the first message and on ready (a repeat is harmless). The relay signs as the player, so it
// forwards ONLY /v2/race/, judged on the built Uri (HostNetPathRule). Escape pauses (GameWindow.Escape.cs), a second
// Escape inside 2 s panics, and panic closes it with every other game (PanicSurfaces "games").
// Not here yet: MergedAccountRecovery on a merged-account answer (the board host lacks it too).
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal const string GlassroadId = "glassroad";

        /// <summary>The door switch's raw value; tests swap it rather than the process env.</summary>
        internal static Func<string?> GlassroadDoorEnv { get; set; } =
            () => Environment.GetEnvironmentVariable(GlassroadHostRules.DoorEnv);

        /// <summary>The live read of the door switch. Never throws.</summary>
        internal static bool GlassroadDoorOpen() { try { return GlassroadHostRules.DoorOpen(GlassroadDoorEnv()); } catch { return false; } }

        /// <summary>WPF LauncherCatalogue "race": with the switch on, every Racing Thoughts door opens Glassroad.</summary>
        internal static string ResolveDoor(string id) =>
            string.Equals(id, "race", StringComparison.OrdinalIgnoreCase) && GlassroadDoorOpen() ? GlassroadId : id;

        /// <summary>A direct launch of the page needs the switch too.</summary>
        internal static bool GlassroadGate()
        {
            if (GlassroadDoorOpen()) return true;
            Log.Information("GlassroadHost: launch refused, {Env} is not 1", GlassroadHostRules.DoorEnv);
            return false;
        }

        internal static Action<string> GlassroadNoteEvent { get; set; } = key => App.FeatureDayLog?.Note(key);

        private bool IsGlassroad => string.Equals(Spec.Id, GlassroadId, StringComparison.OrdinalIgnoreCase);
        private bool _grOpen, _grEarlyInitSent, _grPinged;
        private DateTime _grLastBeatUtc = DateTime.UtcNow, _grLastProgressUtc = DateTime.UtcNow;
        private DispatcherTimer? _grWatch;

        /// <summary>Test seam: the last net relay; done once its reply is queued on the UI thread.</summary>
        internal Task GlassroadPending { get; private set; } = Task.CompletedTask;

        /// <summary>WPF Launch after the window exists: watchdog, presence, the load-time init.</summary>
        private void OpenGlassroad()
        {
            if (_grOpen) return;
            _grOpen = true;
            _grEarlyInitSent = _grPinged = false;
            _grLastBeatUtc = _grLastProgressUtc = DateTime.UtcNow;
            Closed += (_, _) => CloseGlassroad();
            Web.NavigationCompleted += url => { if (PageUrl != null && SameOrigin(url, PageUrl)) PostGlassroadInit(); };
            _grWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _grWatch.Tick += (_, _) => CheckGlassroadWatch(DateTime.UtcNow);
            _grWatch.Start();
            PrizeOwnership.Changed += OnGlassroadOwned;
            _grHaptics = CoreHaptics.Service;
            if (_grHaptics != null) _grHaptics.ConnectionChanged += OnGlassroadToys;
            try { GlassroadNoteEvent(ConditioningControlPanel.Models.SeasonFeatureKeys.ToDayLogEvent(
                ConditioningControlPanel.Models.SeasonFeatureKeys.Race3d)!); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { FriendsHead.Service?.EnterActivity(PresenceActivity.Race); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("GlassroadHost: launched");
        }

        /// <summary>WPF Close: the one funnel every exit reaches (page, title-bar X, panic, boot failure, watchdog).</summary>
        private void CloseGlassroad()
        {
            if (!_grOpen) return;
            _grOpen = false;
            PrizeOwnership.Changed -= OnGlassroadOwned;
            if (_grHaptics != null) _grHaptics.ConnectionChanged -= OnGlassroadToys;
            _grHaptics = null;
            GlassroadHapticDirector.Stop();
            try { _grWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _grWatch = null;
            try { FriendsHead.Service?.LeaveActivity(PresenceActivity.Race); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("GlassroadHost: closed");
        }

        private HapticService? _grHaptics;
        private static bool GlassroadToysConnected => CoreSettings.Current.Haptics is { Enabled: true }
            && CoreHaptics.Service is { IsConnected: true };
        private void OnGlassroadOwned() => Dispatcher.UIThread.Post(() =>
        {
            if (_grOpen && !_closing) Post(GlassroadHostRules.OwnedFrame(PrizeOwnership.Grants));
        });
        private void OnGlassroadToys(object? sender, bool connected) => Dispatcher.UIThread.Post(() =>
        {
            if (_grOpen && !_closing) Post(new { type = "toys", connected = GlassroadToysConnected });
        });

        /// <summary>The init frame, straight out (this head has no ready queue).</summary>
        internal void PostGlassroadInit()
        {
            try
            {
                var s = CoreSettings.Current;
                var uid = CoreAccount.UnifiedUserId;
                bool online = !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(PbpAuthToken());
                Post(GlassroadHostRules.InitFrame(online ? uid : null, s.UserDisplayName, GlassroadTier(),
                    GentleVisuals.On, s.GentleFadeSeconds, s.GentleSpeed, PrizeOwnership.Grants, GlassroadToysConnected));
            }
            catch (Exception ex) { Log.Debug("Glassroad: init post failed: {E}", ex.Message); }
        }

        /// <summary>The server's last effective tier, else the entitlement this head knows (display only: the
        /// lobby decides every gate itself).</summary>
        private static int GlassroadTier()
        {
            try { return CoreSettings.Current.LastKnownEffectiveTier ?? (CoreEntitlement.HasLab ? 2 : CoreEntitlement.HasPremium ? 1 : 0); }
            catch { return 0; }
        }

        /// <summary>WPF OnPageMessage. True = claimed; boot-error and log fall through to the shell frames.</summary>
        private bool HandleGlassroad(JObject o)
        {
            if (_closing) return true;
            _grLastProgressUtc = DateTime.UtcNow;
            var type = (string?)o["type"];
            try
            {
                if (!_grEarlyInitSent && !IsReady && type != "ready")
                {
                    _grEarlyInitSent = true;
                    PostGlassroadInit();   // backstop for a load-time init that beat the bridge
                }
                switch (type)
                {
                    case "ready":
                        IsReady = true;
                        _grLastBeatUtc = DateTime.UtcNow;
                        _grPinged = false;
                        PostGlassroadInit();
                        try { Web.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
                        return true;
                    case "heartbeat":
                    case "pong":
                        _grLastBeatUtc = DateTime.UtcNow;
                        _grPinged = false;
                        return true;
                    case "toy":
                    case "toy-bed":
                    case "toy-stop":
                        GlassroadHapticDirector.OnPageMessage(o);
                        return true;
                    case "open-prize-counter":
                        return true; // B3: no shop on this head.
                    case "net":
                        OnGlassroadNet(o);
                        return true;
                    case "exit":
                    case "menu-back":
                        // The page's own Leave / the menu's Back link. Escape never sends these.
                        Close();
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Glassroad: message handler threw ({Type}): {E}", type, ex.Message);
                return true;
            }
            return false;
        }

        /// <summary><c>net { id, method, path, body }</c> -&gt; <c>net-reply { id, status, body }</c>. A refusal or
        /// failure answers status 0 (the page's "no answer"); no id goes unanswered.</summary>
        private void OnGlassroadNet(JObject o)
        {
            var id = (string?)o["id"] ?? "";
            var path = (string?)o["path"] ?? "";
            var method = ((string?)o["method"] ?? "GET").Trim().ToUpperInvariant();
            var b = o["body"];
            var body = b == null || b.Type == JTokenType.Null ? ""
                : b.Type == JTokenType.String ? (string?)b ?? "" : b.ToString(Newtonsoft.Json.Formatting.None);
            if (method != "GET" && method != "POST") { ReplyGlassroadNet(id, 0, "forbidden_method"); return; }

            var baseUrl = PbpServerBase();
            if (string.IsNullOrEmpty(baseUrl)) { ReplyGlassroadNet(id, 0, ""); return; }   // a sandbox with no loopback
            // Signed first (the host names the caller), then the SIGNED path is judged on its built Uri.
            if (!GlassroadHostRules.TrySign(method, path, body, CoreAccount.UnifiedUserId, out var signedPath, out var signedBody)
                || !HostNetPathRule.TryBuild(baseUrl, signedPath, GlassroadHostRules.AllowedPathPrefix, out var target)
                || target is null)
            {
                Log.Warning("Glassroad: net REJECTED for path '{Path}'", path);
                ReplyGlassroadNet(id, 0, "forbidden_path");
                return;
            }

            var token = PbpAuthToken();
            var http = PbpHttpClient();
            GlassroadPending = Task.Run(async () =>
            {
                int status = 0;
                string reply = "";
                try
                {
                    using var request = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, target);
                    if (method == "POST") request.Content = new StringContent(signedBody, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                    request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                    using var response = await http.SendAsync(request).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                    reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (status, reply) = (0, "");
                    Log.Debug("Glassroad: net {Method} {Path} failed: {E}", method, path, ex.Message);
                }
                Dispatcher.UIThread.Post(() => ReplyGlassroadNet(id, status, reply));
            });
        }

        private void ReplyGlassroadNet(string id, int status, string body)
            { if (!_closing) Post(new { type = "net-reply", id, status, body }); }

        /// <summary>One 5 s watchdog tick (WPF StartWatch): before ready, 45 s with no page message at all closes
        /// it (the bridge beats every 2 s); after ready, silent past 20 s gets one ping, then the close.</summary>
        internal string CheckGlassroadWatch(DateTime nowUtc)
        {
            if (!_grOpen || _closing) return "idle";
            if (PbpHostRules.BootTimedOut(IsReady, nowUtc - _grLastProgressUtc))
            {
                Log.Warning("Glassroad: no page progress for {S:0}s - closing", PbpHostRules.BootDeadline.TotalSeconds);
                Close();
                return "boot-timeout";
            }
            switch (PbpHostRules.HeartbeatStep(IsReady, (nowUtc - _grLastBeatUtc).TotalSeconds, _grPinged))
            {
                case PbpHostRules.WatchStep.Ping:
                    _grPinged = true;
                    Post(new { type = "ping", t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                    return "ping";
                case PbpHostRules.WatchStep.Close:
                    Log.Warning("Glassroad: page heartbeat silent >20s and no pong - closing");
                    Close();
                    return "closed";
                case PbpHostRules.WatchStep.Ok:
                    return "ok";
                default:
                    return "idle";
            }
        }
    }
}
