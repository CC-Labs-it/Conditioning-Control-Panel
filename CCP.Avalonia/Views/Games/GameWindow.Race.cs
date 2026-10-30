// Racing Thoughts on the shared GameWindow: WPF Services/Chaos/CaucusHostService.cs (7.1.5), the host half
// that needs nothing this head lacks. Launch's door (:132 RacingAccess.CanLaunch), boot (:255 init +
// manifest), run-started / run-ended with the one-payout latch (:336, :431), the ping-then-close heartbeat
// watchdog (:595) and the teardown funnel (:686). Shell frames the race shares (boot-error, exit's 1200 ms
// watchdog, exit-done) stay in GameWindow.HandleMessage, and panic closes it with every other game.
// ponytail: WPF MainWindow.TryRacePauseOnEscape (:1505) keeps a lone Escape as the race's pause; here the
// first Escape still panics the race closed (only Piece by Piece is ported, MainShellWindow.PanicKey.cs).
// ponytail: still missing on this head, each its own unit (parity row game-race): BambiCloud levels
// (cloud-open/cloud-start, settings.cloud stays false so the page hides the verb), the track player and
// its decoder seed (own tracks + charting are GameWindow.RaceTracks.cs), sfx / fire-payload (no ChaosSfx bank or payload bridge here), favorites +
// loom-list, report-bug, fullscreen-set, the mandatory-video pause, the meta frame, bubble credit and SeasonRecap TrackFeature.
using System;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Race;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>WPF CaucusHostService.Launch (:132): no racing purchase, no race.</summary>
        internal static bool RaceGate()
        {
            if (RacingAccess.CanLaunch) return true;
            Log.Information("RaceHost: launch refused, no racing purchase");
            return false;
        }

        /// <summary>Spark banking (WPF DtrhMetaBridge.AwardRun, real state: ChaosMeta.AwardRunRewards); tests swap it.</summary>
        internal static Func<ChaosMeta.ChaosRunRewardInput, int> RaceBankSparks = input => ChaosMeta.AwardRunRewards(input);

        private bool IsRace => string.Equals(Spec.Id, "race", StringComparison.OrdinalIgnoreCase);
        private readonly RaceRunLifecycle _raceRun = new();
        private DispatcherTimer? _raceHeartbeatWatch;
        private DateTime _raceLastHeartbeatUtc;
        private int[] _raceOwnedTracks = Array.Empty<int>();
        private bool _racePinged, _raceExiting, _raceStarted, _raceDisposed;

        internal bool RaceRunActive => _raceRun.IsActive;
        /// <summary>The watchdog clock (P08); tests step it.</summary>
        internal static Func<DateTime> RaceUtcNow = () => DateTime.UtcNow;
        internal bool RaceWatchRunning => _raceHeartbeatWatch?.IsEnabled == true;

        /// <summary>WPF Launch after the gate: fresh run state, heartbeat watch, presence. On Opened.</summary>
        private void StartRace()
        {
            if (_raceStarted) return;
            _raceStarted = true;
            _raceOwnedTracks = RacingAccess.OwnedTracks;
            PrizeGrants.GrantsChanged += OnRaceGrantsChanged;
            _raceRun.Reset();
            _raceExiting = _racePinged = false;
            _raceLastHeartbeatUtc = RaceUtcNow();
            _raceHeartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _raceHeartbeatWatch.Tick += (_, _) => CheckRaceHeartbeat(RaceUtcNow());
            _raceHeartbeatWatch.Start();
            try { Platform.FriendsHead.Service?.EnterActivity(PresenceActivity.Race); }   // WPF :223
            catch (Exception ex) { Log.Debug("RaceHost: presence enter: {E}", ex.Message); }
            Log.Information("CaucusHostService: launched");
        }

        // WPF CaucusHostService.OnRaceGrantsChanged: additions update the menu; any loss closes
        // the host, even when another owned track would still allow launch.
        private void OnRaceGrantsChanged() => Dispatcher.UIThread.Post(() =>
        {
            if (_raceDisposed || _closing) return;
            var owned = RacingAccess.OwnedTracks;
            if (!RacingAccess.CanLaunch || _raceOwnedTracks.Except(owned).Any()) { Close(); return; }
            if (_raceOwnedTracks.SequenceEqual(owned)) return;
            _raceOwnedTracks = owned;
            Post(new { type = "race-ownership", tracks = owned });
        });

        /// <summary>WPF OnPageMessage (:317). True = claimed; false falls through to the shell frames.</summary>
        private bool HandleRace(JObject o)
        {
            var type = (string?)o["type"];
            if (HandleRaceTrack(type, o)) return true;
            switch (type)
            {
                case "ready":
                    IsReady = true;
                    OnRacePageReady();
                    return true;
                case "heartbeat":
                case "pong":
                    _raceLastHeartbeatUtc = RaceUtcNow();
                    _racePinged = false;
                    return true;
                case "run-started":
                    if (_raceRun.TryStart()) Log.Information("RaceHost: run started (seed={Seed})", (string?)o["seed"]);
                    return true;
                case "run-ended":
                    OnRaceRunEnded(o);
                    return true;
                case "exit":
                    _raceExiting = true;   // the shell arms the 1200 ms close (WPF ArmExitWatchdog)
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>WPF OnPageReady (:255): init with the race's settings block, then the manifest.</summary>
        private void OnRacePageReady()
        {
            _raceLastHeartbeatUtc = RaceUtcNow();
            _racePinged = false;
            Post(RaceInitMessage());
            try
            {
                var server = _server;
                var m = (_library ??= new(() => server == null ? new() : GameMediaManifest.BuildLocal(server.AssetUrl))).Value;
                Post(GameMediaManifest.Frame(m));
            }
            catch (Exception ex) { Log.Warning("CaucusHostService.OnPageReady: {E}", ex.Message); }
        }

        internal object RaceInitMessage() => new
        {
            type = "init",
            protocol = 1,
            settings = new
            {
                masterVolume = RaceSafe(() => CoreSettings.Current.MasterVolume, 100),
                // Reduced and Off both read as reduced motion: the page has no third state.
                reducedMotion = RaceSafe(() => CoreSettings.Current.MotionLevel != MotionLevel.Full, false),
                // WPF sends true (:278): only a host that owns the levels browser offers it; not yet here.
                cloud = false,
                racingTracks = RacingAccess.OwnedTracks,
                returnToCasino = false,   // no Back Room cabinet on this head
            },
            modId = RaceSafe(() => CoreMods.ActiveModId, "builtin-sissyhypno"),
            modContent = (object?)null,   // WPF DtrhModContent.BuildInitPayload: null = shipped assets
        };

        private static T RaceSafe<T>(Func<T> read, T fallback)
        {
            try { return read(); } catch { return fallback; }
        }

        /// <summary>WPF OnRunEnded (:431): the descent's formula with the score divided by 5, under the
        /// same 250 XP a minute ceiling.</summary>
        internal static (int BaseXp, int FinalXp) RacePayout(double score, double durationSec, double skillMult)
        {
            score = Math.Max(0, score);
            durationSec = Math.Max(1, durationSec);
            int baseXp = (int)Math.Min(score / 5.0, 250.0 * (durationSec / 60.0));
            return (baseXp, (int)Math.Round(baseXp * skillMult));
        }

        /// <summary>run-ended -> XP + Sparks, answered with payout-result. One payout per run.</summary>
        private void OnRaceRunEnded(JObject o)
        {
            if (!_raceRun.TryEnd()) return;
            try
            {
                double score = Math.Max(0, (double?)o["score"] ?? 0);
                double durationSec = Math.Max(1, (double?)o["durationSec"] ?? 60);
                double skillMult = ChaosRunState.SkillMultProvider?.Invoke() ?? 1.0;
                var pay = RacePayout(score, durationSec, skillMult);
                long previousBest = ChaosMeta.State.BestScore;
                int sparksEarned = RaceBankSparks(new ChaosMeta.ChaosRunRewardInput(
                    RunDurationSec: durationSec, DifficultyMult: 1.0, SparkGainMult: 1.0, Score: score / 5.0,
                    TrickleDrops: 0, DripFeedMaxed: false, BestCombo: (int?)o["bestCombo"] ?? 0,
                    Defused: (int?)o["effects"] ?? 0, ElapsedSec: durationSec));
                CoreProgression.AddXP(pay.BaseXp, "Chaos");   // WPF :469 AddXP(baseXp, XPSource.Chaos)
                Post(new
                {
                    type = "payout-result", baseXp = pay.BaseXp, skillMult, finalXp = pay.FinalXp,
                    sparksEarned, previousBest, dryRun = false,
                });
                Log.Information("RaceHost: run complete: score {Score:0} over {Dur:0}s -> {Base} XP, {Sparks} sparks",
                    score, durationSec, pay.BaseXp, sparksEarned);
            }
            catch (Exception ex) { Log.Warning("RaceHost.OnRunEnded: {E}", ex.Message); }
        }

        /// <summary>One WPF StartHeartbeatWatch tick (:600). Guarded on IsReady: a loading page cannot
        /// trip it. 10 s mid-run, 20 s on the menu; silent past that gets one ping, then the close.</summary>
        internal string CheckRaceHeartbeat(DateTime nowUtc)
        {
            if (!IsReady || _raceExiting || _closing) return "idle";
            double limit = _raceRun.IsActive ? 10 : 20;
            if ((nowUtc - _raceLastHeartbeatUtc).TotalSeconds <= limit) return "ok";
            if (!_racePinged)
            {
                _racePinged = true;
                Post(new { type = "ping" });
                return "pinged";
            }
            Log.Warning("RaceHost: page heartbeat silent >{Limit}s and no pong - closing", limit);
            Close();
            return "closed";
        }

        /// <summary>WPF DisposeAll (:686): the one funnel every exit reaches (Closed). Idempotent.</summary>
        private void DisposeRace()
        {
            if (!_raceStarted || _raceDisposed) return;
            _raceDisposed = true;
            PrizeGrants.GrantsChanged -= OnRaceGrantsChanged;
            try { _raceHeartbeatWatch?.Stop(); } catch { }
            _raceHeartbeatWatch = null;
            try { DisposeRaceTracks(); }
            catch (Exception ex) { Log.Debug("RaceHost: track teardown: {E}", ex.Message); }
            _raceRun.Reset();
            Log.Information("CaucusHostService: closed");
            try { Platform.FriendsHead.Service?.LeaveActivity(PresenceActivity.Race); }   // WPF :722
            catch (Exception ex) { Log.Debug("RaceHost: presence leave: {E}", ex.Message); }
        }
    }
}
