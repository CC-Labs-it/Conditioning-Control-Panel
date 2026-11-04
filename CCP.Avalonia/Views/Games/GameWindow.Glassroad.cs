// PORTED from ConditioningControlPanel/Services/Glassroad/GlassroadHostService.cs (feat/glassroad-a 5121933ee):
// GLASSROAD, the 3D race (Assets/web/glassroad/index.html). The launcher uses this host behind its Lab gate;
// the older direct race route still uses the CCP_GLASSROAD preview switch (GameWindow.Launch).
// INIT GOES OUT BEFORE READY: the page awaits init at boot (3 s, then defaults with no net) but says ready only once
// shaders are warm, so init is posted at the page's load (NavigationCompleted, this engine's nearest seam to WPF's
// DOMContentLoaded), on the first message and on ready (a repeat is harmless). The relay signs as the player, so it
// forwards ONLY /v2/race/, judged on the built Uri (HostNetPathRule). Escape pauses (GameWindow.Escape.cs), a second
// Escape inside 2 s panics, and panic closes it with every other game (PanicSurfaces "games").
// Not here yet: MergedAccountRecovery on a merged-account answer (the board host lacks it too).
using System;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Glassroad;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Sparkles;
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

        /// <summary>The older direct race route on this head; the launcher uses the current WPF route.</summary>
        internal static string ResolveDoor(string id) =>
            string.Equals(id, "race", StringComparison.OrdinalIgnoreCase) && GlassroadDoorOpen() ? GlassroadId : id;

        /// <summary>WPF GlassroadHostService.PrimeAllowed: the launcher padlock uses the Lab bar.</summary>
        internal static bool LauncherRaceLocked() => !TierGate.RequiresLab(Loc.Get("launcher_game_race_title")).Allowed;

        /// <summary>A direct launch of the page needs the switch too.</summary>
        internal static bool GlassroadGate()
        {
            if (GlassroadDoorOpen()) return true;
            Log.Information("GlassroadHost: launch refused, {Env} is not 1", GlassroadHostRules.DoorEnv);
            return false;
        }

        private bool IsGlassroad => string.Equals(Spec.Id, GlassroadId, StringComparison.OrdinalIgnoreCase);
        private bool _grOpen, _grEarlyInitSent, _grPinged;
        private readonly System.Collections.Generic.Queue<string> _grJoins = new();
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
            try { FriendsHead.Service?.EnterActivity(PresenceActivity.Race); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("GlassroadHost: launched");
        }

        /// <summary>WPF Close: the one funnel every exit reaches (page, title-bar X, panic, boot failure, watchdog).</summary>
        private void CloseGlassroad()
        {
            if (!_grOpen) return;
            _grOpen = false;
            try { _grWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _grWatch = null;
            try { FriendsHead.Service?.LeaveActivity(PresenceActivity.Race); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("GlassroadHost: closed");
        }

        /// <summary>The init frame, straight out (this head has no ready queue).</summary>
        internal void PostGlassroadInit()
        {
            try
            {
                var s = CoreSettings.Current;
                var uid = CoreAccount.UnifiedUserId;
                bool online = !string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(PbpAuthToken());
                Post(GlassroadHostRules.InitFrame(online ? uid : null, s.UserDisplayName, GlassroadTier(),
                    GentleVisuals.On, s.GentleFadeSeconds, s.GentleSpeed));
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
                        while (_grJoins.Count > 0) Post(new { type = "join-room", roomId = _grJoins.Dequeue() });
                        try { Web.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
                        return true;
                    case "heartbeat":
                    case "pong":
                        _grLastBeatUtc = DateTime.UtcNow;
                        _grPinged = false;
                        return true;
                    case "net":
                        OnGlassroadNet(o);
                        return true;
                    case "friends-list": // WPF GlassroadHostService.cs:225-229
                        Post(new { type = "friends-list-reply", id = (string?)o["id"] ?? "",
                            friends = GlassroadRules.FriendRows(FriendsHead.Service?.Snapshot.Friends) });
                        return true;
                    case "invite-friend":
                        GlassroadPending = InviteGlassroadFriend(o);
                        return true;
                    case "race-result":
                        ClaimGlassroadResult(o);
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

        // WPF GlassroadHostService.cs:314-343: only a known friend and a valid room can be sent.
        private async Task InviteGlassroadFriend(JObject o)
        {
            var id = (string?)o["id"] ?? "";
            var uid = (string?)o["friendUid"];
            var room = (string?)o["roomId"];
            var outcome = new SendOutcome(SendResult.Refused, null);
            try
            {
                var svc = FriendsHead.Service;
                if (svc != null && !string.IsNullOrEmpty(uid) && svc.Snapshot.Friends.Any(f => f.Id == uid)
                    && InviteDestination.IsRoomId(room))
                    outcome = await svc.InviteToRaceAsync(uid, room!).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Debug("Glassroad invite failed: {E}", ex.Message); outcome = new(SendResult.TryLater, null); }
            void Reply()
            {
                if (_closing) return;
                try
                {
                    var note = GlassroadRules.Sentence(Loc.Get(GlassroadRules.InviteNoteKey(outcome)));
                    Post(new { type = "invite-friend-reply", id,
                        sent = global::ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.IsGood(outcome.Result), note });
                }
                catch (Exception ex) { Log.Debug("Glassroad invite reply failed: {E}", ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Reply(); else Dispatcher.UIThread.Post(Reply);
        }

        // WPF GlassroadHostService.cs:253-266: the server prices a finished race and returns the slip.
        private void ClaimGlassroadResult(JObject o)
        {
            var result = GlassroadRules.SparklesClaimFor(o);
            if (result == null) return;
            var claim = SparklesClaim.Create(GlassroadRules.SparklesGame, result.Mode, result.Result,
                (long)result.Duration.TotalMilliseconds, result.Detail, result.Proof);
            var identity = FriendsHead.Identity();
            var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
                Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar));
            var http = PbpHttpClient();
            GlassroadPending = Task.Run(async () =>
            {
                try
                {
                    var outcome = await SparklesClaimService.ClaimAsync(claim, identity, url, http).ConfigureAwait(false);
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { DeliverGlassroadReceipt(outcome, identity?.UnifiedId); }
                        catch (Exception ex) { Log.Debug("Glassroad slip failed: {E}", ex.Message); }
                    });
                }
                catch (Exception ex) { Log.Debug("Glassroad claim failed: {E}", ex.Message); }
            });
        }

        private void DeliverGlassroadReceipt(SparklesClaimOutcome outcome, string? account)
        {
            var r = outcome.Receipt;
            var s = CoreSettings.Current;
            if (r is { Shadow: false, Credited: > 0, Sp: >= 0 } && account != null &&
                V2WalletAdoption.Decide(FriendsHead.Identity()?.UnifiedId == account, false, r.Sp.Value, s.SkillPoints, out var next))
            {
                int before = s.SkillPoints;
                s.SkillPoints = next;
                CoreSettings.Save();
                SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
            }
            var slip = SparklesSlip.Build(outcome, GentleVisuals.On, Loc.Get);
            if (slip?["sp"]?.Type == JTokenType.Integer && s.SkillPoints > (long)slip["sp"]!) slip["sp"] = s.SkillPoints;
            if (!_closing && slip != null) Post(slip);
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
