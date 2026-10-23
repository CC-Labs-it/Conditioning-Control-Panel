// Racing Thoughts on the shared GameWindow: WPF Services/Chaos/CaucusHostService.cs (7.1.5), the host half
// that needs nothing this head lacks. Launch's door (:132 RacingAccess.CanLaunch), boot (:255 init +
// manifest), run-started / run-ended with the one-payout latch (:336, :431), the ping-then-close heartbeat
// watchdog (:595) and the teardown funnel (:686). Shell frames the race shares (boot-error, exit's 1200 ms
// watchdog, exit-done) stay in GameWindow.HandleMessage, and panic closes it with every other game.
// Remaining parity gaps are recorded once in docs/avalonia-parity.md (game-race).
using System;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
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
                    RacePlaySfx((string?)o["name"], (float?)o["scale"] ?? 0.6f);
                    return true;
                case "fire-payload":
                    // WPF audio payload reuses FlashSubliminal; visual kinds (including video) stay in-world.
                    try
                    {
                        if (string.Equals((string?)o["kind"], "audio", StringComparison.OrdinalIgnoreCase)
                            && CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                    }
                    catch (Exception ex) { Log.Warning("RaceHost.FirePayload: {E}", ex.Message); }
                    return true;
                case "fullscreen-set":
                    RaceQueue(() =>
                    {
                        WindowState = (bool?)o["on"] == true ? global::Avalonia.Controls.WindowState.FullScreen : global::Avalonia.Controls.WindowState.Normal;
                        Post(new { type = "fullscreen", on = WindowState == global::Avalonia.Controls.WindowState.FullScreen });
                        Web.Focus();
                    });
                    return true;
                case "report-bug":
                    RaceQueue(() =>
                    {
                        var report = new Windows.BugReportWindow();
                        Closed += (_, _) => report.Close();
                        _ = report.ShowDialogSafe(this);
                    });
                    return true;
                case "run-started":
                    if (_raceRun.TryStart())
                    {
                        // Race is outside the season badge Catalog: WPF TrackFeature only notes its day-log event.
                        App.FeatureDayLog?.Note(SeasonFeatureKeys.ToDayLogEvent(SeasonFeatureKeys.Race)!);
                        Log.Information("RaceHost: run started (seed={Seed})", (string?)o["seed"]);
                    }
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
                var m = _server == null ? new GameMediaManifest.Manifest() : GameMediaManifest.BuildActive(WebAssetServer.AssetPath);
                DtrhModContent.MergeMedia(m);
                Post(GameMediaManifest.Frame(m));
                var favorites = DtrhAssetStatsStore.TopAssets(12);
                if (favorites.Count > 0) Post(new { type = "favorites", names = favorites });
                PostRaceLoomList();
                DtrhLoomStore.Changed -= OnRaceLoomChanged;
                DtrhLoomStore.Changed += OnRaceLoomChanged;
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
            modContent = DtrhModContent.BuildInitPayload(),
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
                    type = "meta", rev = ++_raceMetaRevision, testMode = false,
                    state = JObject.Parse(JsonConvert.SerializeObject(ChaosMeta.State, new JsonSerializerSettings
                    { ContractResolver = new CamelCasePropertyNamesContractResolver() })),
                });
                try { CoreProgression.AddXP(pay.BaseXp, "Chaos"); }
                catch (Exception ex) { Log.Debug("RaceHost payout AddXP: {E}", ex.Message); }
                try { CreditRaceBubbles((int?)o["popped"] ?? 0); }
                catch (Exception ex) { Log.Debug("RaceHost bubble credit: {E}", ex.Message); }
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

        internal Windows.AchievementPopup? RaceMilestonePopup { get; private set; }

        private void CreditRaceBubbles(int count)
        {
            if (count <= 0 || App.Achievements is not { } achievements) return;
            int milestones = achievements.TrackBubblesPopped(count);
            if (milestones > 0)
            {
                var settings = CoreSettings.Current;
                int previous = settings.SkillPoints;
                settings.SkillPoints += milestones;
                CoreSettings.Save();
                SparklePointRewards.PublishCredit(previous, settings.SkillPoints, SparklePointSource.BubbleMilestone);
                RaceMilestonePopup?.Close();
                int boundary = achievements.Progress.TotalBubblesPopped / 100 * 100;
                RaceMilestonePopup = new Windows.AchievementPopup(Loc.GetF("achievement_bubble_milestone_name", boundary),
                    Loc.Get("achievement_bubble_milestone_flavor"), "bubble_pop.png",
                    headerText: Loc.Get("achievement_bubble_milestone_header"));
                RaceMilestonePopup.FindControl<IconGlyph>("TxtHeaderIcon")!.Kind = IconKind.Sparkle;
                RaceMilestonePopup.Show();
            }
            App.Quests?.TrackBubblesPopped(count);
        }

        private int _raceMetaRevision;

        internal static GameWindow? RaceInFront()
        {
            lock (Open) return Open.FirstOrDefault(w => w.IsRace && w.IsActive);
        }

        internal bool RaceReady => IsReady && !_raceExiting && !_closing;

        // WPF CloseActive: page winds down first, then a 1200 ms watchdog. A second close forces it.
        internal void CloseRaceGracefully()
        {
            RaceMilestonePopup?.Close();
            foreach (var report in OwnedWindows.OfType<Windows.BugReportWindow>().ToArray()) report.Close();
            if (!RaceReady) { Close(); return; }
            _raceExiting = true;
            Post(new { type = "exit-request" });
            RaceRunOnce(() => { if (!_raceDisposed) Close(); }, TimeSpan.FromMilliseconds(1200));
        }

        // Test seam for the watchdog (P08): tests capture and fire it instead of sleeping.
        internal static Action<Action, TimeSpan> RaceRunOnce = (a, d) => DispatcherTimer.RunOnce(a, d);

        private void RaceQueue(Action action) => Dispatcher.UIThread.Post(() =>
        {
            if (_raceDisposed || _closing || _raceExiting) return;
            try { action(); }
            catch (Exception ex) { Log.Debug("RaceHost: queued action: {E}", ex.Message); }
        });

        // Same native-video boundary as BouncingTextOverlay; closed races are never retained by the video host.
        internal static void PauseRacesForVideo(bool on)
        {
            GameWindow[] races;
            lock (Open) races = Open.Where(w => w.IsRace).ToArray();
            foreach (var race in races) race.RaceQueue(() =>
            {
                race.Post(new { type = "pause", on });
                if (!on) race.Web.Focus();
            });
        }

        private void OnRaceLoomChanged() => RaceQueue(PostRaceLoomList);

        private void PostRaceLoomList() => Post(new
        {
            type = "loom-list",
            spirals = DtrhLoomStore.List().Select(s => new
            {
                slug = s.Slug, url = "/ccp.spirals/loom_" + s.Slug + ".gif",
                @params = ParseRaceLoomParams(s.ParamsJson),
            }),
        });

        private static JObject? ParseRaceLoomParams(string? json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json); }
            catch { return null; }
        }

        // Adapted from parity/trunk ChaosSfx (4a6a2581), generic Play only: the race has no other caller.
        private static void RacePlaySfx(string? name, float scale)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':', '.', '\0' }) >= 0) return;
            try
            {
                var rel = "chaos/" + name + ".mp3";
                var mod = CoreModArt.ModAudioFile(rel, CoreMods.ActiveModPackage?.InstalledPath);
                var path = !string.IsNullOrEmpty(mod) && System.IO.File.Exists(mod) ? mod
                    : ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", rel));
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                    CoreAudio.PlayOneShot(path, Math.Clamp(CoreSettings.Current.MasterVolume / 100f * scale, 0f, 1f), "chaos-sfx");
            }
            catch (Exception ex) { Log.Debug("RaceHost.sfx: {E}", ex.Message); }
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
            try { _raceHeartbeatWatch?.Stop(); } catch { }
            _raceHeartbeatWatch = null;
            DtrhLoomStore.Changed -= OnRaceLoomChanged;
            RaceMilestonePopup?.Close();
            try { DisposeRaceTracks(); }
            catch (Exception ex) { Log.Debug("RaceHost: track teardown: {E}", ex.Message); }
            _raceRun.Reset();
            Log.Information("CaucusHostService: closed");
            try { Platform.FriendsHead.Service?.LeaveActivity(PresenceActivity.Race); }   // WPF :722
            catch (Exception ex) { Log.Debug("RaceHost: presence leave: {E}", ex.Message); }
        }
    }
}
