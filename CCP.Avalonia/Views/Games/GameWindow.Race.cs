// Racing Thoughts on the shared GameWindow: WPF Services/Chaos/CaucusHostService.cs (7.1.5), the host half
// that needs nothing this head lacks. Launch's door (:132 RacingAccess.CanLaunch), boot (:255 init +
// manifest), run-started / run-ended with the one-payout latch (:336, :431), the ping-then-close heartbeat
// watchdog (:595) and the teardown funnel (:686). Shell frames the race shares (boot-error, exit's 1200 ms
// watchdog, exit-done) stay in GameWindow.HandleMessage, and panic closes it with every other game.
// ponytail: cloud levels, Back Room handoff, creator media and season hooks
// remain tracked explicitly in docs/avalonia-parity.md; do not advertise unavailable cloud levels.
using System;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
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
            EmiState.NoteUsage("race");
            try { _ = App.ReleaseContent?.RequestPackAsync(ReleaseContentService.PackAudioWeb); }
            catch (Exception ex) { Log.Debug("RaceHost: audio-web request: {E}", ex.Message); }
            _raceOwnedTracks = RacingAccess.OwnedTracks;
            PrizeOwnership.Changed += OnRaceOwnershipChanged;
            _raceVideo = CoreEngine.Video;
            if (_raceVideo != null)
            {
                _raceVideo.VideoStarted += OnRaceVideoStarted;
                _raceVideo.VideoEnded += OnRaceVideoEnded;
            }
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
                case "sfx":
                    if ((string?)o["name"] is { Length: > 0 } name)
                        ChaosOverlayWindow.ChaosSfx.Play(name, (float?)o["scale"] ?? 0.6f);
                    return true;
                case "fire-payload":
                    FireRacePayload(o);
                    return true;
                case "fullscreen-set":
                    WindowState = (bool?)o["on"] == true ? WindowState.FullScreen : WindowState.Normal;
                    Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                    return true;
                case "report-bug":
                    var report = new BugReportWindow();
                    _raceReport?.Close();
                    _raceReport = report;
                    _ = report.ShowDialogSafe(this);
                    return true;
                case "run-started":
                    if (_raceRun.TryStart()) Log.Information("RaceHost: run started (seed={Seed})", (string?)o["seed"]);
                    return true;
                case "run-ended":
                    OnRaceRunEnded(o);
                    return true;
                case "exit":
                    RaceStopTrack();
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
            Web.Focus();
            Post(RaceInitMessage());
            try
            {
                var server = _server;
                var m = (_library ??= new(() => server == null ? new() : GameMediaManifest.BuildLocal(server.AssetUrl))).Value;
                Post(GameMediaManifest.Frame(m));
            }
            catch (Exception ex) { Log.Warning("CaucusHostService.OnPageReady: {E}", ex.Message); }
            var favorites = DtrhAssetStatsStore.TopAssets(12);
            if (favorites.Count > 0) Post(new { type = "favorites", names = favorites });
            PostRaceLoom();
            DtrhLoomStore.Changed -= OnRaceLoomChanged;
            DtrhLoomStore.Changed += OnRaceLoomChanged;
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
            RaceStopTrack();
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
                Post(new
                {
                    type = "meta", rev = ++_raceMetaRev, testMode = false,
                    state = JObject.Parse(JsonConvert.SerializeObject(ChaosMeta.State,
                        new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })),
                });
                CoreProgression.AddXP(pay.BaseXp, "Chaos");   // WPF :469 AddXP(baseXp, XPSource.Chaos)
                CreditRaceBubbles((int?)o["popped"] ?? 0);
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

        private static void CreditRaceBubbles(int count)
        {
            if (count <= 0) return;
            try
            {
                int milestones = App.Achievements?.TrackBubblesPopped(count) ?? 0;
                if (milestones > 0)
                {
                    var settings = CoreSettings.Current;
                    int before = settings.SkillPoints;
                    settings.SkillPoints += milestones;
                    CoreSettings.Save();
                    SparklePointRewards.PublishCredit(before, settings.SkillPoints, SparklePointSource.BubbleMilestone);
                    int total = App.Achievements!.Progress.TotalBubblesPopped;
                    // WPF AchievementService.cs:512: the sparkle header, not the trophy one.
                    var popup = new AchievementPopup(Loc.GetF("achievement_bubble_milestone_name", total - total % 100),
                        Loc.Get("achievement_bubble_milestone_flavor"), "bubble_pop.png",
                        headerText: Loc.Get("achievement_bubble_milestone_header"));
                    popup.FindControl<global::ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtHeaderIcon")!.Kind = IconKind.Sparkle;   // WPF "✨" (IconMap)
                    RaceShowPopup(popup);
                }
                App.Quests?.TrackBubblesPopped(count);
            }
            catch (Exception ex) { Log.Debug("RaceHost bubble credit: {E}", ex.Message); }
        }

        /// <summary>Shows the milestone popup; tests capture it.</summary>
        internal static Action<Window> RaceShowPopup = p => p.Show();

        private MandatoryVideoScheduler? _raceVideo;
        private void OnRaceVideoStarted() => PostRacePause(true);
        private void OnRaceVideoEnded() => PostRacePause(false);
        private void PostRacePause(bool on) => Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _raceDisposed) return;
            Post(new { type = "pause", on });
            if (!on) Web.Focus();
        });

        private int _raceMetaRev;
        private int[] _raceOwnedTracks = Array.Empty<int>();
        private BugReportWindow? _raceReport;
        internal bool RaceReady => IsReady && !_raceExiting && !_closing;
        internal static GameWindow? RaceInFront()
        {
            lock (Open) return Open.FirstOrDefault(w => w.IsRace && w.IsActive);
        }

        // WPF CaucusHostService.FirePayload: only native audio; all visual kinds stay in-world.
        private static void FireRacePayload(JObject o)
        {
            if (!string.Equals((string?)o["kind"], "audio", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var payload = EffectPayloadFactory.Build(EffectBubblePayloadKind.Audio);
                payload.Strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);
                payload.DurationMult = Math.Clamp((double?)o["durationMult"] ?? 1, 0.1, 10);
                payload.Fire();
            }
            catch (Exception ex) { Log.Warning("RaceHost.FirePayload: {E}", ex.Message); }
        }

        // WPF OnRaceGrantsChanged: additions refresh the menu; loss closes gracefully.
        private void OnRaceOwnershipChanged() => Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _raceDisposed) return;
            var owned = RacingAccess.OwnedTracks;
            if (!RacingAccess.CanLaunch || _raceOwnedTracks.Except(owned).Any())
            {
                if (IsReady && !_raceExiting)   // WPF CloseActive (:239): a ready page gets exit-request + watchdog
                {
                    _raceExiting = true;
                    Post(new { type = "exit-request" });
                    DispatcherTimer.RunOnce(() => { if (!_closing) Close(); }, TimeSpan.FromMilliseconds(1200));
                }
                else Close();   // not ready (or already exiting): WPF DisposeAll at once
                return;
            }
            if (_raceOwnedTracks.SequenceEqual(owned)) return;
            _raceOwnedTracks = owned;
            Post(new { type = "race-ownership", tracks = owned });
        });

        private void OnRaceLoomChanged() => Dispatcher.UIThread.Post(() =>
        {
            if (RaceReady && !_raceDisposed) PostRaceLoom();
        });

        private void PostRaceLoom()
        {
            Post(new
            {
                type = "loom-list",
                spirals = DtrhLoomStore.List().Select(s => new
                {
                    slug = s.Slug, url = "/__loom/loom_" + s.Slug + ".gif",
                    @params = ParseRaceLoomParams(s.ParamsJson),
                }),
            });
        }

        private static JObject? ParseRaceLoomParams(string? json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json); }
            catch { return null; }
        }

        /// <summary>WPF DisposeAll (:686): the one funnel every exit reaches (Closed). Idempotent.</summary>
        private void DisposeRace()
        {
            if (!_raceStarted || _raceDisposed) return;
            _raceDisposed = true;
            PrizeOwnership.Changed -= OnRaceOwnershipChanged;
            if (_raceVideo != null)
            {
                _raceVideo.VideoStarted -= OnRaceVideoStarted;
                _raceVideo.VideoEnded -= OnRaceVideoEnded;
                _raceVideo = null;
            }
            DtrhLoomStore.Changed -= OnRaceLoomChanged;
            _raceReport?.Close();
            _raceReport = null;
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
