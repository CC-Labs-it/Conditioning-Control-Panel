// PORTED from ConditioningControlPanel/Services/PieceByPiece/PieceByPieceHostService.cs (WPF 7.1.5).
// The pure half (net whitelist, frames, reservoir, watchdog ladder) is Core Services/PieceByPiece/PbpHostRules;
// this is the window glue. Not here yet (game-pbp-b): online pictures (.Media.cs: pbp:media-state /
// media-flavour / media-more / online-media), friend / Lobby intents (.Friends.cs),
// MergedAccountRecovery and SeasonRecapService.TrackFeature.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.PieceByPiece.Wardrobe;
using ConditioningControlPanel.Services.Quiz;
using ConditioningControlPanel.Services.Stakes;
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
        internal static GameWindow? ForegroundPbp()
        {
            lock (Open) return Open.FirstOrDefault(w => w.Spec.Id == PbpId && w.IsActive && !w._closing);
        }

        internal void PostPbpKeptEscape()
        {
            if (!IsReady || _closing) return;
            Post(new { type = "pbp:escape" });
            Web.FocusPage();
        }

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

        private bool _pbpOpen;
        private Task? _pbpBootFrames;   // single-flight: settings always precedes identity, even during an OS query
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
            PrizeOwnership.GrantsChanged += OnPbpGrantsChanged;
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
            PrizeOwnership.GrantsChanged -= OnPbpGrantsChanged;
            _pbpEquipLifetime.Cancel();
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
                // PvP stakes (WPF :388): the Core bridge talks to /v2/stakes/* itself and books a lost time
                // stake once the match settles. Online PvP only; the page never sends these solo or hotseat.
                if (StakeBridge.Handles(type))
                {
                    _pbpStakes ??= PbpStakeBridge("pbp", PostPbpStake);
                    PbpStakePending = _pbpStakes.Handle(o);
                    return true;
                }
                switch (type)
                {
                    case "ready":
                        IsReady = true;
                        PbpPending = OnPbpReady();
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
                    case PbpLookRules.LookType:
                        PickPbpLook(o);
                        return true;
                    case PbpLookRules.CounterType:
                        // B3: the Prize Counter is WPF desktop only in 7.2.
                        return true;
                    case "pbp:net":
                        OnPbpNetRequest(o);
                        return true;
                    case PbpWatchRules.SettingFrame:
                        if (PbpWatchRules.TryRead(o, out var key, out var value)
                            && PbpWatchRules.Apply(CoreSettings.Current, key, value))
                            CoreSettings.Service?.Save();
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

        /// <summary>The web engine reads the OS motion preference on both platforms. Tests supply the same answer.</summary>
        internal Func<Task<bool>>? PbpOsAnimations { get; set; }

        private async Task<bool> ReadPbpOsAnimations()
        {
            var result = await Web.InvokeScriptAsync("!window.matchMedia('(prefers-reduced-motion: reduce)').matches");
            // No engine / no answer: WPF's failed OS read also defaults to animations enabled.
            return !string.Equals(Unwrap(result ?? ""), "false", StringComparison.OrdinalIgnoreCase);
        }

        private Task OnPbpReady()
        {
            Log.Information("PieceByPieceHost: page ready");
            PbpBootFailedThisSession = false;
            _pbpLastBeatUtc = _pbpLastProgressUtc = DateTime.UtcNow;
            _pbpPinged = false;
            try { Web.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
            return _pbpBootFrames ??= PostPbpBootFrames();
        }

        private async Task PostPbpBootFrames()
        {
            bool osAnimations = true;
            try { osAnimations = await (PbpOsAnimations?.Invoke() ?? ReadPbpOsAnimations()); }
            catch (Exception ex) { Diag.Swallowed(ex); }
            if (_closing) return;
            var s = CoreSettings.Current;
            var withoutGentle = s.MotionLevel == MotionLevel.Off ? MotionLevel.Off
                : !osAnimations ? MotionLevel.Reduced : s.MotionLevel;
            var frame = PbpHostRules.SettingsFrame(s.VideoMinDurationSeconds,
                withoutGentle != MotionLevel.Full || s.GentleVisuals, PbpWhispers());
            frame["letPeopleWatch"] = PbpWatchRules.LetPeopleWatch(s);
            frame["watchEffects"] = PbpWatchRules.WatchEffects(s);
            frame["watchAmount"] = PbpWatchRules.WatchAmount(s);
            frame["gentleIntroSpeed"] = PbpWatchRules.GentleIntroSpeed(s.GentleVisuals, withoutGentle, s.GentleSpeed);
            Post(frame);
            Post(PbpHostRules.IdentityFrame(CoreAccount.UnifiedUserId, PbpAuthToken(),
                s.UserDisplayName, CoreReleaseContent.AppVersion));
            PostPbpWardrobe();
            PbpEquipPending = PullPbpEquip();
        }

        /// <summary>WPF SafeWhisperClips: own brain drain first, then the mod's permitted fallback.</summary>
        private IReadOnlyList<string> PbpWhispers()
        {
            if (_server == null) return Array.Empty<string>();
            try
            {
                static IEnumerable<string>? Names(string? folder) =>
                    !string.IsNullOrEmpty(folder) && Directory.Exists(folder)
                        ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)) : null;
                bool shared = CoreSettings.Current.SubAudioAudible && ModAudioPolicy.UsesSharedSubAudio(CoreMods.ActiveModId);
                // Unchecked own clips are not offered (the server 404s them), so all-unchecked falls back like an empty folder.
                var assets = CorePaths.EffectiveAssets;
                var off = IntakeRun.DisabledAssetSet(_server.DisabledAssets());
                var own = Path.Combine(assets, PbpWhisperClips.BrainDrainFolder);
                return PbpWhisperClips.Build(Names(own)?.Where(n => IntakeRun.IsAssetActive(off, assets, Path.Combine(own, n))),
                    shared, Names(_server.SubAudioRoot()), Names(_server.WordsRoot())).Select(_server.WhisperUrl).ToArray();
            }
            catch (Exception ex)
            {
                Log.Debug("PieceByPiece: whisper clips failed: {E}", ex.Message);
                return Array.Empty<string>();
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

        // ---- stakes (WPF PieceByPieceHostService :650-660) ----

        /// <summary>One bridge for the life of the app (WPF static <c>_stakes</c>): a settle watch it started keeps
        /// running (and books) after the board closes, so panic or an exit never strands a settled loss.</summary>
        private static StakeBridge? _pbpStakes;

        private static Func<string, Action<JObject>, StakeBridge> _pbpStakeBridge = StakeBridge.ForApp;

        /// <summary>Test seam: how the bridge is built (the app's is <see cref="StakeBridge.ForApp"/>). Setting it
        /// drops the cached bridge.</summary>
        internal static Func<string, Action<JObject>, StakeBridge> PbpStakeBridge
        {
            get => _pbpStakeBridge;
            set { _pbpStakeBridge = value; _pbpStakes = null; }
        }

        /// <summary>Test seam: the last stake frame's work (replies are posted on the UI thread when it ends).</summary>
        internal static Task PbpStakePending { get; private set; } = Task.CompletedTask;

        /// <summary>WPF PostStake: a <c>stake</c> frame to the open board, on the UI thread; a closed board is a quiet no-op.</summary>
        private static void PostPbpStake(JObject o) => Dispatcher.UIThread.Post(() =>
        {
            GameWindow? w;
            lock (Open) w = Open.FirstOrDefault(x => x.Spec.Id == PbpId && x._pbpOpen);
            try { w?.Post(o); }
            catch (Exception ex) { Log.Debug("PieceByPiece: stake post failed: {E}", ex.Message); }
        });
    }
}
