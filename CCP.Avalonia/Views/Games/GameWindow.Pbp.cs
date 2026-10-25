// PORTED from ConditioningControlPanel/Services/PieceByPiece/PieceByPieceHostService.cs (WPF 7.1.5).
// The pure half (net whitelist, frames, reservoir, watchdog ladder) is Core Services/PieceByPiece/PbpHostRules;
// this is the window glue. Not here yet (game-pbp-b): online pictures (.Media.cs: pbp:media-state /
// media-flavour / media-more / online-media), whisper clips (needs asset-server routes for ccp.subaudio /
// ccp.words; the settings frame sends none, which the page reads as "no whispers"), friend / Lobby intents (.Friends.cs), the kept Escape (pbp:escape),
// MergedAccountRecovery and SeasonRecapService.TrackFeature.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Stakes;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Piece by Piece host (WPF PieceByPieceHostService), frames exactly as WPF sends and answers them:
    /// at <c>ready</c> <c>pbp:settings</c> then <c>pbp:identity</c> (WPF OnPageReady :295-313, no shell
    /// <c>init</c>); <c>pbp:net</c> -&gt; <c>pbp:net-result</c> with the token attached here and only
    /// <c>/v2/pbp/*</c> GET / POST forwarded (:559-626); <c>pbp:media-request</c> -&gt; <c>pbp:media</c> from the
    /// player's own library (:447); <c>pbp:exit</c> closes at once (:422); <c>heartbeat</c> / <c>pong</c> /
    /// <c>ping</c>: 20 s silence, one ping, then close, and a 45 s progress-aware boot deadline (:694-745).
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string PbpId = "piecebypiece";

        /// <summary>One client for the app session (WPF :119). 30 s covers the events long poll.</summary>
        private static readonly Lazy<HttpClient> PbpHttp = new(() =>
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}"); }
            catch (Exception ex) { Diag.Swallowed(ex); }
            return c;
        });

        /// <summary>Test seam: the transport the net lane sends through.</summary>
        internal static Func<HttpClient> PbpHttpClient { get; set; } = () => PbpHttp.Value;

        /// <summary>Where <c>/v2/pbp/*</c> lives on this head: the proxy, or null in a sandbox with no loopback
        /// (FriendsHead.BaseUrl), which answers every call status 0.</summary>
        internal static Func<string?> PbpServerBase { get; set; } = () =>
            FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
                Environment.GetEnvironmentVariable(FriendsHead.EnvVar));

        private static readonly Random PbpRng = new();

        // WPF: app lifetime, so a settle watch survives closing the board. Replies go to the
        // current board (or nowhere), and the shared settlement deduplicates across reopen.
        internal static StakeBridge? PbpStakes { get; set; }
        internal Task StakePending { get; private set; } = Task.CompletedTask;

        private static void PostPbpStake(JObject reply) => Dispatcher.UIThread.Post(() =>
        {
            GameWindow? board;
            lock (Open) board = Open.FirstOrDefault(w => w.Spec.Id == PbpId);
            if (board != null && !board._closing) board.Post(reply);
        });

        private bool _pbpOpen;
        private bool _pbpSettingsPosted;   // pbp:settings goes out exactly once per boot
        private bool _pbpIdentityPosted;   // and so does pbp:identity
        private bool _pbpPinged;
        private DateTime _pbpLastBeatUtc = DateTime.UtcNow;
        private DateTime _pbpLastProgressUtc = DateTime.UtcNow;
        private DispatcherTimer? _pbpWatch;
        private Lazy<(string Rel, string Url, bool IsImage)[]>? _pbpLibrary;   // walked once per window, off the UI thread

        /// <summary>Test seam: the last off-thread media / net reply; done once its frame is queued on the UI thread.</summary>
        internal System.Threading.Tasks.Task PbpPending { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

        /// <summary>The page's LAST boot attempt failed (WPF BootFailedThisSession), cleared by a boot that reaches ready.</summary>
        internal static bool PbpBootFailedThisSession { get; private set; }

        /// <summary>WPF Launch (:185), the part after the window exists. Free for everyone (no tier gate, no account).</summary>
        private void OpenPbp()
        {
            if (_pbpOpen) return;
            _pbpOpen = true;
            _pbpLastBeatUtc = _pbpLastProgressUtc = DateTime.UtcNow;
            Closed += (_, _) => ClosePbp();
            _pbpWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _pbpWatch.Tick += (_, _) => CheckPbpWatch(DateTime.UtcNow);
            _pbpWatch.Start();
            try { FriendsHead.Service?.EnterActivity(PresenceActivity.Chess); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("PieceByPieceHost: launched");
        }

        /// <summary>WPF Close (:270): the one funnel every exit reaches (title-bar X, pbp:exit, boot error,
        /// watchdog, panic). Nothing here is authoritative, so there is nothing to flush.</summary>
        private void ClosePbp()
        {
            if (!_pbpOpen) return;
            _pbpOpen = false;
            try { _pbpWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _pbpWatch = null;
            try { FriendsHead.Service?.LeaveActivity(PresenceActivity.Chess); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("PieceByPieceHost: closed");
        }

        /// <summary>WPF OnPageMessage (:378). True = claimed; boot-error falls through to the shell's close.</summary>
        private bool HandlePbp(JObject o)
        {
            _pbpLastProgressUtc = DateTime.UtcNow;   // every message is a sign of life: the boot deadline is progress-aware
            var type = (string?)o["type"];
            try
            {
                if (StakeBridge.Handles(type))
                {
                    PbpStakes ??= StakeBridge.ForApp("pbp", PostPbpStake);
                    StakePending = PbpStakes.Handle(o);
                    return true;
                }
                switch (type)
                {
                    case "ready":
                        IsReady = true;
                        OnPbpReady();
                        return true;
                    case "heartbeat":
                    case "pong":
                        _pbpLastBeatUtc = DateTime.UtcNow;
                        _pbpPinged = false;
                        return true;
                    case "boot-error":
                        PbpBootFailedThisSession = true;
                        return false;
                    case "pbp:exit":
                        Close();
                        return true;
                    case "pbp:media-request":
                        OnPbpMediaRequest(o);
                        return true;
                    case "pbp:net":
                        OnPbpNetRequest(o);
                        return true;
                }
            }
            catch (Exception ex)
            {
                // Crash-safe by construction: one malformed frame must never take the window with it.
                Log.Warning("PieceByPiece: message handler threw ({Type}): {E}", type, ex.Message);
                return true;
            }
            return false;
        }

        private void OnPbpReady()
        {
            Log.Information("PieceByPieceHost: page ready");
            PbpBootFailedThisSession = false;
            _pbpLastBeatUtc = _pbpLastProgressUtc = DateTime.UtcNow;
            _pbpPinged = false;
            try { Web.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
            if (!_pbpSettingsPosted)
            {
                _pbpSettingsPosted = true;
                var s = CoreSettings.Current;
                Post(PbpHostRules.SettingsFrame(s.VideoMinDurationSeconds, s.MotionLevel != MotionLevel.Full, Array.Empty<string>()));
            }
            if (!_pbpIdentityPosted)
            {
                _pbpIdentityPosted = true;
                Post(PbpHostRules.IdentityFrame(CoreAccount.UnifiedUserId, PbpAuthToken(),
                    CoreSettings.Current.UserDisplayName, CoreReleaseContent.AppVersion));
            }
        }

        /// <summary>The CCP auth token, NOT the Patreon bearer; empty with no cloud session (WPF SafeAuthToken :632).</summary>
        private static string PbpAuthToken()
        {
            try { return CoreSettings.Current.AuthToken ?? string.Empty; } catch { return string.Empty; }
        }

        /// <summary>WPF OnMediaRequest (:447): the player's own library, local disk only, any list may be empty.</summary>
        private void OnPbpMediaRequest(JObject o)
        {
            var kinds = PbpHostRules.ReadKinds(o["kinds"]);
            var count = PbpHostRules.ReadCount(o);
            if (_pbpLibrary == null)
            {
                var (server, assets, disabled) = (_server, CorePaths.EffectiveAssets, CoreSettings.Current.DisabledAssetPaths?.ToArray());
                _pbpLibrary = new(() => PbpLibrary(server, assets, disabled).ToArray());
            }
            var library = _pbpLibrary;
            PbpPending = Task.Run(() =>
            {
                (string[] Images, string[] Gifs, string[] Videos) media;
                try { lock (PbpRng) media = PbpHostRules.SampleMedia(library.Value, kinds, count, PbpRng); }
                catch (Exception ex) { Log.Debug("PieceByPiece: media walk failed: {E}", ex.Message); media = (Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()); }
                Dispatcher.UIThread.Post(() => { if (!_closing) Post(PbpHostRules.MediaFrame(media)); });
                Log.Debug("PieceByPiece: media reply {I} images, {G} gifs, {V} videos", media.Images.Length, media.Gifs.Length, media.Videos.Length);
            });
        }

        /// <summary>The active pool (WPF DtrhAssetManifest.EnumerateActive; here GameMediaManifest, the port's one authority on it).</summary>
        private static IEnumerable<(string Rel, string Url, bool IsImage)> PbpLibrary(WebAssetServer? server, string assets,
            IEnumerable<string>? disabled)
        {
            if (server == null) return Array.Empty<(string, string, bool)>();
            // Videos too: WPF EnumerateActive walks both trees (BuildLocal is the picture-only Breakout deal).
            var m = GameMediaManifest.Build(assets, server.AssetUrl, disabled);
            return m.Images.Select(e => (e.Name, e.Url, true)).Concat(m.Videos.Select(e => (e.Name, e.Url, false)));
        }

        /// <summary>WPF OnNetRequest (:559): never leaves a call unanswered (an id with no reply sits in the page's pending map for 45 s).</summary>
        private void OnPbpNetRequest(JObject o)
        {
            var call = PbpHostRules.ReadNet(o);
            if (call.Refusal != null)
            {
                Log.Warning("PieceByPiece: net REJECTED ({Why}) for {Method} '{Path}'", call.Refusal, call.Method, call.Path);
                Post(PbpHostRules.NetResult(call.Id, 0, call.Refusal));
                return;
            }
            var token = PbpAuthToken();
            var baseUrl = PbpServerBase();
            var http = PbpHttpClient();
            PbpPending = Task.Run(async () =>
            {
                var (status, body) = await PbpHostRules.SendAsync(http, baseUrl, call, token, CoreReleaseContent.AppVersion).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => { if (!_closing) Post(PbpHostRules.NetResult(call.Id, status, body)); });
            });
        }

        /// <summary>One 5 s tick of both WPF watchdogs (ArmBootDeadline :694, StartHeartbeatWatch :717). Returns what it did.</summary>
        internal string CheckPbpWatch(DateTime nowUtc)
        {
            if (!_pbpOpen || _closing) return "idle";
            if (PbpHostRules.BootTimedOut(IsReady, nowUtc - _pbpLastProgressUtc))
            {
                PbpBootFailedThisSession = true;
                Log.Warning("PieceByPiece: page boot-error: boot deadline: no progress for {S:0}s", PbpHostRules.BootDeadline.TotalSeconds);
                Close();
                return "boot-timeout";
            }
            switch (PbpHostRules.HeartbeatStep(IsReady, (nowUtc - _pbpLastBeatUtc).TotalSeconds, _pbpPinged))
            {
                case PbpHostRules.WatchStep.Ping:
                    _pbpPinged = true;
                    Post(new { type = "ping" });
                    return "ping";
                case PbpHostRules.WatchStep.Close:
                    Log.Warning("PieceByPiece: page heartbeat silent >20s and no pong - closing");
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
