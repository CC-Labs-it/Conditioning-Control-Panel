// Down the Rabbit Hole on the shared GameWindow: WPF Services/Chaos/DtrhHostService.cs, slice A - the
// host half the first descent needs. Launch's Lab door (LauncherCatalogue.cs:241), boot (:271 init with
// motionLevel + runSetup, then the manifest :83), request-run -> run-config (:523, rules in Core
// DtrhRunWire), run-started / run-progress / run-ended with the one-payout latch (:360, :583) and the
// close-mid-run booking (:1002 DtrhRunCloseRule). The kept Escape (:119) is posted by the panic key's pause
// claim (MainShellWindow.PanicKey.cs, Core PanicPolicy.KeptEscapePage.Dtrh); a full panic closes it with
// every other game (PanicSurfaces "games"). Shell frames (boot-error, exit's 1200 ms close, exit-done)
// stay in GameWindow.HandleMessage.
// Slice B: the manifest's online half (Core DtrhRemoteMedia: online media copied to this machine, re-posted as
// copies land, loops lane, loops-dry back-off) and root-relative asset urls the page's WebGL layer may draw.
// ponytail: slice game-dtrh-host-c, each its own unit (parity row game-dtrh): the meta bridge (DtrhMetaBridge:
// meta snapshot, meta-command, the Warren shelves), the ccp.art / ccp.spirals / ccp.content / ccp.mod origins, loom-list and
// loom-*, favorites + asset-stats, the heartbeat watchdog + relaunch-once recovery, bark routing, sfx,
// fire-payload, freeze/mute (native video), vn-speaking, haptics, fullscreen-set, report-bug, session stats,
// achievements bubble credit, RevealService.Sync, crash sentinel, EMI desk fires, the audio-web pack request,
// tucking the panel to the tray, and SeasonRecap TrackFeature.
using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal const string DtrhId = "dtrh";

        /// <summary>WPF LauncherCatalogue.cs:241: Lab tier, or the server's daily-free DtRH drop; refused with
        /// the tier toast (TierGate.DemandLab).</summary>
        internal static bool DtrhGate() => TierGate.DemandLab(Loc.Get("launcher_game_dtrh_title"), DtrhId);

        /// <summary>The launcher's padlock probe for the same door, without the toast.</summary>
        internal static bool DtrhLocked() => !TierGate.RequiresLab(Loc.Get("launcher_game_dtrh_title"), DtrhId).Allowed;

        /// <summary>Spark banking (WPF DtrhMetaBridge.AwardRun -> ChaosMeta.AwardRunRewards); tests swap it.</summary>
        internal static Func<ChaosMeta.ChaosRunRewardInput, bool, int> DtrhBankSparks =
            (input, countsAsRun) => ChaosMeta.AwardRunRewards(input, countsAsRun);

        /// <summary>The host's own clock inside a descent (P08); tests step it.</summary>
        internal static Func<DateTime> DtrhUtcNow = () => DateTime.UtcNow;

        private bool IsDtrh => string.Equals(Spec.Id, DtrhId, StringComparison.OrdinalIgnoreCase);
        private bool _dtrhRunActive, _dtrhExiting;
        private JObject? _dtrhLastProgress;
        private DateTime _dtrhRunStartedUtc = DateTime.MinValue;

        internal bool DtrhRunActive => _dtrhRunActive;

        /// <summary>WPF IsReady: the page said ready and is not on its way out.</summary>
        internal bool DtrhReady => IsReady && !_dtrhExiting && !_closing;

        /// <summary>WPF IsInFront: the open DtRH window when it is the active one, else null.</summary>
        internal static GameWindow? DtrhInFront()
        {
            lock (Open) return Open.FirstOrDefault(w => w.IsDtrh && w.IsActive);
        }

        // WPF MainWindow.xaml.cs:1719-1727; DtrhHostService.cs:250-266,1017-1023.
        internal static bool TryDtrhPanic()
        {
            GameWindow? live;
            lock (Open) live = Open.FirstOrDefault(w => w.IsDtrh);
            if (live == null) return false;
            if (live.IsReady && !live._dtrhExiting)
            {
                live._dtrhExiting = true;
                live.Post(new { type = "end-run", reason = "host" });
                var timer = DispatcherTimer.RunOnce(live.Close, TimeSpan.FromMilliseconds(1200));
                live.Closed += (_, _) => timer.Dispose();
            }
            else live.Close();
            return true;
        }

        // WPF PostKeptEscape (:119): GameWindow.Escape.cs, shared with the race, the board and Breakout.

        /// <summary>WPF OnPageMessage (:321). True = claimed; false falls through to the shell frames.</summary>
        private bool HandleDtrh(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    OnDtrhPageReady();
                    return true;
                case "heartbeat":
                case "pong":
                    return true;   // the watchdog lands with game-dtrh-host-b
                case "request-run":
                    OnDtrhRequestRun(o);
                    return true;
                case "run-progress":
                    // The page's ~10 s snapshot: a window that dies without a word banks this instead.
                    if (_dtrhRunActive) _dtrhLastProgress = o;
                    return true;
                case "run-started":
                    _dtrhRunActive = true;
                    _dtrhLastProgress = null;
                    _dtrhRunStartedUtc = DtrhUtcNow();
                    Log.Information("DtrhHost: run started (diff={D}, mode={M})", (string?)o["difficulty"] ?? "Gentle", (string?)o["mode"]);
                    return true;
                case "run-ended":
                    OnDtrhRunEnded(o, fromTeardown: false);
                    return true;
                case "exit":
                    _dtrhExiting = true;   // the shell arms the 1200 ms close (WPF ArmExitWatchdog)
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>WPF OnPageReady (:271): init, then the manifest.</summary>
        private void OnDtrhPageReady()
        {
            try { Web.Focus(); }   // Escape (pause / hold-to-exit) works from the first frame
            catch (Exception ex) { Log.Debug("DtrhHost: focus: {E}", ex.Message); }
            Post(DtrhInitMessage());
            try { PostDtrhManifest(); }
            catch (Exception ex) { Log.Warning("DtrhHostService.OnPageReady: {E}", ex.Message); }
            // WPF :303: online media copied to this machine after this frame reaches the page as a fresh manifest
            // (the page replaces its pools), so the tube fills without a relaunch. Unsubscribed in DisposeDtrh (P68).
            DtrhRemoteMedia.CopiesReady -= OnDtrhCopiesReady;
            DtrhRemoteMedia.CopiesReady += OnDtrhCopiesReady;
        }

        /// <summary>WPF PostManifest (:83): the library, then the online half on top (Core DtrhRemoteMedia). Urls are
        /// root-relative (WebAssetServer.AssetPath), the shape hostMedia.js counts as local and lets WebGL draw.</summary>
        private void PostDtrhManifest()
        {
            var m = _server == null ? new GameMediaManifest.Manifest() : GameMediaManifest.BuildActive(WebAssetServer.AssetPath);
            if (_server != null)
            {
                var root = CorePaths.EffectiveAssets;
                DtrhRemoteMedia.AppendTo(m, CoreSettings.Current, copy => WebAssetServer.AssetPath(Path.GetRelativePath(root, copy)),
                    DtrhCopier);
            }
            Post(GameMediaManifest.Frame(m));
        }

        /// <summary>Where online copies come from; tests swap it (no network in a test).</summary>
        internal static DtrhRemoteMedia.Copier DtrhCopier = DtrhRemoteMedia.AssetsTempCopier;

        /// <summary>WPF OnRemoteCopiesReady (:97): off the UI thread; re-post while the page is up and not leaving.</summary>
        private void OnDtrhCopiesReady() => Dispatcher.UIThread.Post(() =>
        {
            try { if (DtrhReady) PostDtrhManifest(); }
            catch (Exception ex) { Log.Debug("DtrhHost: manifest re-post failed: {E}", ex.Message); }
        });

        internal object DtrhInitMessage() => new
        {
            type = "init",
            protocol = 1,
            settings = new { masterVolume = RaceSafe(() => CoreSettings.Current.MasterVolume, 100) },
            // The player's Motion setting (full / reduced / off) for shared/motion.js.
            motionLevel = RaceSafe(DtrhMotionWire.Current, DtrhMotionWire.Full),
            modId = RaceSafe(() => CoreMods.ActiveModId, "builtin-sissyhypno"),
            modContent = (object?)null,   // WPF DtrhModContent.BuildInitPayload: null = shipped assets
            runSetup = DtrhRunWire.RunSetup(CoreSettings.Current),
            m2Test = false,
        };

        /// <summary>WPF OnRequestRun (:523): persist the hub's choices, deal the run, answer run-config.</summary>
        private void OnDtrhRequestRun(JObject o)
        {
            try
            {
                if (o["setup"] is JObject setup) DtrhRunWire.PersistRunSetup(setup, CoreSettings.Current);
                var (cfg, scripted, _) = DtrhRunWire.Deal(testMode: false);
                Post(new { type = "run-config", runConfig = DtrhRunWire.RunConfig(cfg, CoreSettings.Current) });
                Log.Information("DtrhHost: dealt run config (diff={D}, scripted={S})", cfg.Difficulty, scripted);
            }
            catch (Exception ex) { Log.Warning("DtrhHost.OnRequestRun: {E}", ex.Message); }
        }

        /// <summary>WPF OnRunEnded (:583): XP + Sparks, answered with payout-result. One payout per descent:
        /// the page's run-ended and the teardown's synthetic one share the latch.</summary>
        private void OnDtrhRunEnded(JObject o, bool fromTeardown)
        {
            bool wasActive = _dtrhRunActive;
            double? hostElapsed = _dtrhRunStartedUtc == DateTime.MinValue ? null : (DtrhUtcNow() - _dtrhRunStartedUtc).TotalSeconds;
            _dtrhRunActive = false;
            _dtrhLastProgress = null;
            _dtrhRunStartedUtc = DateTime.MinValue;
            if (!DtrhRunCloseRule.ShouldPayBooking(wasActive))
            {
                Log.Debug("DtrhHost: run-ended for a descent that is already banked - ignored");
                return;
            }
            try
            {
                var booking = DtrhRunWire.Book(o, hostElapsed);
                double skillMult = ChaosRunState.SkillMultProvider?.Invoke() ?? 1.0;
                double finalXp = booking.BaseXp * skillMult;
                long previousBest = ChaosMeta.State.BestScore;
                int sparksEarned = DtrhBankSparks(booking.RewardInput(o), booking.Payout.CountsAsRun);
                CoreProgression.AddXP(booking.BaseXp, "Chaos");   // WPF AddXP(baseXp, XPSource.Chaos)
                ChaosRank? rankUp = null;
                var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
                if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
                Post(new
                {
                    type = "payout-result", baseXp = booking.BaseXp, skillMult, finalXp, sparksEarned, previousBest,
                    rankUp = rankUp?.ToString(), dryRun = false,
                });
                Log.Information("DtrhHost: web run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP, {Sparks} sparks{T}",
                    booking.BaseXp, skillMult, finalXp, sparksEarned, fromTeardown ? " (banked on close)" : "");
            }
            catch (Exception ex) { Log.Warning("DtrhHost.OnRunEnded: {E}", ex.Message); }
        }

        /// <summary>WPF DisposeAll (:1002): the window died with a descent still falling (frame X, Alt+F4, panic),
        /// so bank the last snapshot as an abandoned booking rather than dropping the run.</summary>
        private void DisposeDtrh()
        {
            if (!IsDtrh) return;
            DtrhRemoteMedia.CopiesReady -= OnDtrhCopiesReady;
            if (DtrhRunCloseRule.ShouldBookOnClose(_dtrhRunActive, _dtrhLastProgress != null))
            {
                Log.Information("DtrhHost: the window closed mid-run - banking the last run snapshot");
                var snapshot = _dtrhLastProgress!;
                snapshot["abandoned"] = true;
                OnDtrhRunEnded(snapshot, fromTeardown: true);
            }
            _dtrhRunActive = false;
            _dtrhExiting = false;
            Log.Information("DtrhHostService: closed");
        }
    }
}
