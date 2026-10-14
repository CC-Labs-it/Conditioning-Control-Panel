using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Reactive companion-dialogue ("bark") system. Modeled on <see cref="GamificationBridge"/>:
    /// a single seam that SUBSCRIBES directly to the events feature services already raise and
    /// decides which bark — if any — should be spoken in response.
    ///
    /// PR1 scope: rule LOADER + full subscription block + counter layer + DRY-RUN matcher.
    /// There is no speak path yet — every decision is logged ("[BARK dry-run] …"), nothing is
    /// rendered to the avatar. The speak path (GigglePriority/Giggle, self-echo mute,
    /// chat-suppression, easter-egg clip) lands in a follow-up PR.
    /// </summary>
    public class BarkService : IDisposable
    {
        private bool _started;
        private readonly object _gate = new();

        // The pure decisions (conditions, gate, rotation, commit) live in Core; this head only feeds
        // it the live reads it owns and renders what it picks.
        private readonly BarkEngine _engine = new()
        {
            EmiAvatarMuted = () => App.EmiDesk?.AvatarMuted == true,
            WhisperPlaying = () => App.Audio?.IsWhisperAudioPlaying == true,
            NarratorPlaying = () => Services.Chaos.ChaosNarrator.IsPlaying,
            AvatarSpeaking = () => App.AvatarWindow?.IsSpeaking ?? false,
            AvatarBusy = ms => App.AvatarWindow?.IsCompanionBusy(ms) ?? false,
            TierName = () => App.Patreon?.CurrentTier.ToString() ?? "None",
            PoolRefPhrases = category => App.Mods?.GetPhrases(category),
        };
        private readonly BarkState _state = new();

        // Subscription teardown (named delegates captured so -= matches +=).
        private readonly List<Action> _unsubscribe = new();
        private readonly List<Action> _engineUnsubscribe = new();
        private readonly List<Action> _trayUnsubscribe = new();

        /// <summary>The Emergency Exit's two bark triggers (Core <see cref="BarkEngine"/> owns the gate exemptions).</summary>
        internal const string EmergencyExitOpenedTrigger = BarkEngine.EmergencyExitOpenedTrigger;
        internal const string EmergencyExitVerdictTrigger = BarkEngine.EmergencyExitVerdictTrigger;

        /// <summary>After rendering a bark, mute its line this long so it can't re-trigger awareness/OCR.</summary>
        private const int SelfEchoMuteMs = 8000;

        /// <summary>
        /// Raised immediately AFTER a bark line has been handed to the avatar (Giggle /
        /// GigglePriority) with the final, substituted text. Fires for every bark that actually
        /// spoke — including the silent mute-egg path — and never for one that a gate suppressed.
        ///
        /// <para>Exists so <c>CompanionBrain</c> can record a <c>BarkEcho</c> turn: the LLM needs to
        /// know what her recorded voice just said, or it will cheerfully repeat it. Combined with the
        /// existing self-echo mute (which stops OCR/keyword triggers hearing her own bubble) this
        /// closes both directions between the two mouths.</para>
        ///
        /// <para>Purely observational — subscribers must not block or throw; the raiser swallows
        /// exceptions so a bad subscriber can never mute a bark.</para>
        /// </summary>
        public event Action<BarkRule, string>? BarkSpoken;

        private void RaiseBarkSpoken(BarkRule rule, string line)
        {
            var handler = BarkSpoken;
            if (handler == null) return;
            try { handler(rule, line); }
            catch (Exception ex) { App.Logger?.Debug("[BARK] BarkSpoken subscriber error: {Error}", ex.Message); }
        }

        private static readonly TimeSpan RapidModSwitchWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RapidClickWindow = TimeSpan.FromSeconds(60);

        // Coin flip is varied per call; not security-sensitive.
        private readonly Random _rng = new();
        private PatreonTier _lastTier = PatreonTier.None; // to detect tier-up for the upgrade egg

        // --- anticipatory SessionSetupReady detector (item E): debounced, fires when the user goes quiet ---
        private System.Windows.Threading.DispatcherTimer? _setupReadyTimer;
        private bool _setupReadyStallPending; // true between the first ready-fire and the stall follow-up
        private const int SetupReadyDebounceMinSec = 8;
        private const int SetupReadyDebounceMaxSec = 12;
        private const int SetupReadyStallSec = 45; // gives setup_ready_stall (setup_idle_sec_gte:45) its window

        /// <summary>
        /// When true the matcher evaluates and logs ("[BARK dry-run] …") but never renders to the
        /// avatar and never writes persisted latches. Speak path is otherwise fully exercised.
        /// </summary>
        public bool DryRun { get => _engine.DryRun; set => _engine.DryRun = value; }

        public BarkState State => _state;

        // ===================== lifecycle =====================

        public void Start()
        {
            if (_started) return;
            _started = true;

            try
            {
                // Opt-in log-only mode for validation: run with CCP_BARK_DRYRUN=1 to exercise the
                // full matcher (logs "[BARK dry-run] …") without rendering to the avatar.
                if (string.Equals(Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN"), "1", StringComparison.Ordinal))
                    DryRun = true;

                _engine.LiveField = ResolveLiveField;
                var rules = BarkRuleLoader.Load();
                _engine.Load(rules); // restores persisted no-repeat rotation so pools don't reset each launch
                // Instant-relaunch egg threshold: relaunched within 60s of last seen.
                _state.CaptureLaunchRecency(App.Settings?.Current?.LastSeenUtc, instantThresholdSeconds: 60);
                _lastTier = App.Patreon?.CurrentTier ?? PatreonTier.None;

                WireSubscriptions();

                // The Descent's four block-driven barks. Attached HERE rather than in
                // App.OnStartup because App.Descent is constructed well before Bark?.Start() runs,
                // and because a watcher with nothing to speak to would be pointless: the seam it
                // calls is on this service. Idempotent, and a no-op when the service is missing.
                Services.Descent.DescentBarkWatcher.Attach();

                App.Logger?.Information(
                    "BarkService started — {Count} rules, {Triggers} trigger keys, dry-run={DryRun}",
                    rules.Count, rules.Triggers.Count(), DryRun);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BarkService failed to start");
            }
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;

            try { _setupReadyTimer?.Stop(); } catch { }
            _setupReadyTimer = null;

            RunUnsubscribers(_unsubscribe);
            RunUnsubscribers(_engineUnsubscribe);
            RunUnsubscribers(_trayUnsubscribe);
        }

        private static void RunUnsubscribers(List<Action> list)
        {
            foreach (var u in list)
            {
                try { u(); } catch (Exception ex) { App.Logger?.Debug(ex, "BarkService: unsubscribe failed"); }
            }
            list.Clear();
        }

        public void Dispose() => Stop();

        /// <summary>
        /// External entry point for the panic/abort key (MainWindow.HandlePanicKeyPress). Raises the
        /// safety-class "Panic" bark, which bypasses the gate and holds the floor.
        /// </summary>
        public void NotifyPanic() => Raise("Panic");

        /// <summary>
        /// The app was just opened — fires the voiced, time-aware welcome greeting. The bucket
        /// (first/soon/back/while/long) reflects how long the user has been away so rules can
        /// select an appropriately-warm line. Returns whether a greeting bark actually spoke so
        /// the caller can fall back to the legacy text-only greeting when the bark system is
        /// unavailable.
        /// </summary>
        public bool NotifyAppOpened(string awayBucket)
            => Raise("AppOpened", c => c.Set("away_bucket", awayBucket ?? "first"));

        /// <summary>
        /// A daily-login-streak milestone (7/14/30/60/100/365 days) was reached for the first
        /// time. Caller owns the once-per-milestone latch (AppSettings.LastAnnouncedStreakMilestone);
        /// guaranteed so it isn't dropped by the min-gap behind the welcome greeting it queues after.
        /// </summary>
        public void NotifyStreakMilestone(int days)
            => Raise("StreakMilestone", c => c.Set("streak_days", (double)days), guaranteed: true);

        /// <summary>
        /// Awareness v2's bark entry point: the arbiter has decided this moment is worth the free,
        /// instant, voiced tier and asks for a line.
        ///
        /// <para>This raises exactly the same <c>ActivityChanged</c>/<c>StillOnActivity</c> triggers the
        /// direct subscriptions used to, with the same context keys, so every authored rule in
        /// <c>bark_rules.json</c> keeps working untouched — the only change is WHO decides. The bark
        /// system's own gates (per-rule cooldown, the 60s global min-gap, chat suppression, safety
        /// hold) still apply on top of the arbiter's, because they are the outer floor for non-safety
        /// lines and v2 tightens pacing, never loosens it.</para>
        ///
        /// <para>Nothing about the frame reaches disk or the network here: bark context values are
        /// substituted into a local, authored line and spoken.</para>
        /// </summary>
        /// <returns>True only when a line actually spoke, so the arbiter records one delivery or none.</returns>
        public bool RaiseAwarenessBark(Awareness.ContextFrame frame)
        {
            if (frame == null) return false;

            // The arbiter records THIS delivery itself (cooldown ledger first, pacing state second),
            // so RecordWithArbiter must not also report it as an external bark — one line, one charge.
            // EMI Desk (MOMENTS 4.B / 3.5): THE AVATAR OWNS VOICE. The arbiter has decided the tube
            // is about to talk about the active window, so EMI goes to blink-only faces for the
            // duration plus the moment's 20s tail. A HOLD, never a line.
            try { App.EmiDesk?.NoteAwarenessReaction(); } catch { }

            _inAwarenessBark = true;
            try
            {
                if (frame.Transition == Awareness.TransitionKind.Milestone)
                {
                    return Raise("StillOnActivity", c => c
                        .Set("activity", frame.ServiceName ?? "")
                        .Set("still_minutes", Math.Floor(Math.Max(0, frame.DwellSeconds) / 60.0))
                        .Set("app_cluster", frame.AppCluster ?? "")
                        .Set("app", frame.AppId ?? ""));
                }

                return Raise("ActivityChanged", c => c
                    .Set("activity", frame.ServiceName ?? "")
                    .Set("category", frame.Category.ToString())
                    .Set("app_cluster", frame.AppCluster ?? "")
                    .Set("app", frame.AppId ?? ""));
            }
            finally { _inAwarenessBark = false; }
        }

        /// <summary>
        /// Re-entrancy guard for the one place a bark is fired ON BEHALF of the arbiter. See
        /// <see cref="CommitFire"/>: every other bark reports itself to the arbiter's shared ledger,
        /// this one is already accounted for there.
        /// </summary>
        private bool _inAwarenessBark;

        /// <summary>
        /// Tells this service that the companion just spoke a line it did not fire — today, an
        /// awareness quip written by the model and delivered straight to the speech bubble.
        ///
        /// <para>The 60s <see cref="GlobalMinGapMs"/> floor is only a floor if it moves for every
        /// ambient line, not just the ones this class produced: without this the next bark trigger
        /// after the awareness bubble closes fires against a stale gap, and a preempting bark can
        /// speak over the bubble outright.</para>
        /// </summary>
        public void NotifyExternalLineSpoken()
        {
            _engine.NoteExternalLineSpoken();
        }

        /// <summary>A dashboard feature popup was opened (feature = control type name w/o "FeatureControl", e.g. "Flash").</summary>
        public void NotifyFeatureOpened(string? feature)
        {
            if (string.IsNullOrEmpty(feature)) return;
            Raise("FeatureOpened", c => c.Set("feature", feature!));
        }

        /// <summary>The user navigated to a tab (tab = ShowTab key, e.g. "leaderboard").</summary>
        public void NotifyTabNavigated(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return;
            Raise("TabNavigated", c => c.Set("tab", tab!));
        }

        /// <summary>A numeric/bool setting changed. Non-numeric props are ignored; value is bool→0/1, enum→int.</summary>
        public void NotifySettingChanged(string? setting)
        {
            if (string.IsNullOrEmpty(setting)) return;
            // Setup-screen tracking for the anticipatory SessionSetupReady bark — ANY setting counts as
            // a "setup action" (even non-numeric ones that the SettingChanged rules below can't match).
            _state.MarkSettingChanged();
            ArmSetupReadyDebounce();
            if (!BarkEngine.TryGetSettingNumber(setting!, out var value)) return;
            Raise("SettingChanged", c => c.Set("setting", setting!).Set("value", value));
        }

        /// <summary>The user changed the avatar appearance/skin (avatar-set selector). Bambi reacts ~1-in-10.</summary>
        public void NotifyAvatarChanged()
        {
            Raise("AvatarChanged");
        }

        /// <summary>The avatar was clicked. Stamps a rolling 60s click count for the escalation eggs.</summary>
        public void NotifyAvatarClicked()
        {
            _state.RegisterAvatarClick();
            _engine.RegisterAvatarClick(); // the engine's clicks_60s condition reads its own window
            Raise("AvatarClicked", c => c.Set("clicks_60s", (double)_state.AvatarClicksWithin(RapidClickWindow)));
        }

        /// <summary>An enhancement was applied to the current browser page (id = enhancement name — no slug field exists).</summary>
        public void NotifyEnhancementApplied(string? enhancementId)
        {
            if (string.IsNullOrEmpty(enhancementId)) return;
            Raise("EnhancementApplied", c => c.Set("enhancement_id", enhancementId!));
        }

        /// <summary>A generic UI action the user took (action = stable key, e.g. "test_video", "reroll_daily", "minimize").</summary>
        public void NotifyUiAction(string? action)
        {
            if (string.IsNullOrEmpty(action)) return;
            Raise("UiAction", c => c.Set("action", action!));
        }

        // ---- Possession (haunted-UI layer of Lockdown; Services/Possession/POSSESSION.md) ----
        // Trigger names are the constants in Possession.PossessionBarkTriggers. Rules live in each
        // pack's bark_rules.json as text-only variants; "target" is what the warden NAMES.
        public void NotifyPossessionRung(int rung) =>
            Raise(Possession.PossessionBarkTriggers.RungChanged, c => c.Set("rung", (double)rung));
        // The packs' PossessionEffect lines use the {target} placeholder (ApplySubstitutions swaps any
        // {key} from this context), so an absent display name must not leave a hole in the sentence -
        // it falls back the way {0} already falls back to "that".
        public void NotifyPossessionEffect(string effect, string? target) =>
            Raise(Possession.PossessionBarkTriggers.Effect, c => { c.Set("effect", effect); c.Set("target", string.IsNullOrWhiteSpace(target) ? "that one" : target!); });
        public void NotifyPossessionTripwire(string kind, int repeat, int total) =>
            Raise(Possession.PossessionBarkTriggers.Tripwire, c => { c.Set("kind", kind); c.Set("repeat", (double)repeat); c.Set("total", (double)total); });
        public void NotifyPossessionWarden(string verb) =>
            Raise(Possession.PossessionBarkTriggers.Warden, c => c.Set("verb", verb));
        public bool NotifyPossessionRules() =>
            Raise(Possession.PossessionBarkTriggers.Rules, guaranteed: true);
        /// <summary>The Emergency Exit sent the user back in with a full timer. ctx: reason (game id), restart (count).</summary>
        public void NotifyPossessionTimerRestarted(string reason, int restart) =>
            Raise(Possession.PossessionBarkTriggers.TimerRestarted, c => { c.Set("reason", reason ?? ""); c.Set("restart", (double)restart); });
        /// <summary>Next launch after a Full Doki lockdown: one line, "I remember." (PossessionRemember).</summary>
        public bool NotifyPossessionRemember() =>
            Raise(Possession.PossessionBarkTriggers.Remember, guaranteed: true);

        /// <summary>The Dose (Services/Haptics/LockdownDoseKeeper.cs): the lockdown switched features on
        /// because nothing was running. ctx: features ("Flash and Subliminals"), round (1-based, 0 when
        /// only the engine was started), engine (1 = the engine itself was started for them).</summary>
        public void NotifyLockdownConscript(string features, int round, bool engineStarted) =>
            Raise(Possession.PossessionBarkTriggers.Conscript, c => { c.Set("features", string.IsNullOrWhiteSpace(features) ? "something" : features); c.Set("round", (double)round); c.Set("engine", engineStarted ? 1.0 : 0.0); });

        // ---- Emergency Exit (the friction door of Lockdown; Services/EmergencyExit/EMERGENCY_EXIT.md) ----
        // Text-only rules in every pack, matched per game via the ctx `game` field (game_eq) the same
        // way the possession rung rules match rung_eq.

        /// <summary>The user pressed EMERGENCY EXIT and a minigame opened. ctx: game, attempt.</summary>
        public void NotifyEmergencyExitOpened(string game, int attempt) =>
            Raise(EmergencyExitOpenedTrigger, c => c.Set("game", game ?? "").Set("attempt", (double)attempt));

        /// <summary>The minigame resolved and the host has ALREADY applied it. ctx: game, outcome
        /// ("escape" = the lockdown is over, "sendback" = the timer went back to full).</summary>
        public void NotifyEmergencyExitVerdict(string game, string outcome) =>
            Raise(EmergencyExitVerdictTrigger, c => c.Set("game", game ?? "").Set("outcome", outcome ?? ""));

        /// <summary>The user opened/refreshed the leaderboard. rank/total let rules react to standing (rank 0 = unranked).</summary>
        public void NotifyLeaderboardViewed(int rank, int total)
        {
            Raise("LeaderboardViewed", c => c.Set("rank", (double)rank).Set("total", (double)total));
        }

        // ===================== The Descent (tap, jar, banked day, Spiral) =====================
        //
        // GARNISH, NOT THE LESSON. Every bark in this block is dropped when the avatar window does
        // not exist, so none of these five may be the only place its mechanic is taught: the vat
        // tooltip, the Spiral room and the help surfaces own that, and these lines sit on top.
        //
        // Edge detection is NOT here. Services.Descent.DescentBarkWatcher listens to the block and
        // decides which single moment an observation is worth; this seam only speaks what it chose,
        // so "once per day" is a property of the persisted memory rather than of a cooldown.

        /// <summary>Today's jar is climbing toward the line that banks the day. ctx: fill_pct, remaining_pct, today_xp, cap.</summary>
        public void NotifyDescentNearBank(Services.Descent.DescentBarkDecision d) =>
            Raise("DescentNearBank", c => c
                .Set("fill_pct", d.FillPct).Set("remaining_pct", d.RemainingPct)
                .Set("today_xp", (double)d.TodayXp).Set("cap", (double)d.Cap)
                .Set("stage", (double)d.Stage).Set("banked_days", (double)d.BankedDays));

        /// <summary>Today crossed the line and is banked. ctx: banked_days, stage, days_to_next, fill_pct.</summary>
        public void NotifyDescentDayBanked(Services.Descent.DescentBarkDecision d) =>
            Raise("DescentDayBanked", c => c
                .Set("banked_days", (double)d.BankedDays).Set("stage", (double)d.Stage)
                .Set("days_to_next", (double)(d.DaysToNext ?? 0)).Set("fill_pct", d.FillPct));

        /// <summary>A new rung on the seven-stage ladder. ctx: stage, banked_days, days_to_next.</summary>
        public void NotifyDescentStageCrossed(Services.Descent.DescentBarkDecision d) =>
            Raise("DescentStageCrossed", c => c
                .Set("stage", (double)d.Stage).Set("banked_days", (double)d.BankedDays)
                .Set("days_to_next", (double)(d.DaysToNext ?? 0)));

        /// <summary>Back after days away, with the relapse bonus paying out. NEVER "gravity", and never a
        /// scolding. ctx: days_away, multiplier, surge_multiplier, banked_days.</summary>
        public void NotifyDescentLapseReturn(Services.Descent.DescentBarkDecision d) =>
            Raise("DescentLapseReturn", c => c
                .Set("days_away", (double)d.DaysAway).Set("multiplier", d.Multiplier)
                .Set("surge_multiplier", d.SurgeMultiplier).Set("banked_days", (double)d.BankedDays));

        /// <summary>The Spiral room was opened for the first time. The rule is a lifetime one-shot, so this
        /// may be called on every entry. ctx: stage, banked_days (0 when the account carries no block).</summary>
        public void NotifyDescentFirstSpiralOpen(Services.Descent.DescentBlock? block) =>
            Raise("DescentFirstSpiralOpen", c => c
                .Set("stage", (double)(block?.Stage?.N ?? 0))
                .Set("banked_days", (double)(block?.Stage?.BankedDays ?? block?.DevotionDays ?? 0)));

        // ===================== Chaos Mode (Lab effect-bubbles roguelite) =====================
        /// <summary>A Chaos run started. ctx: difficulty.</summary>
        public void NotifyChaosRunStarted(string difficulty) => Raise("ChaosRunStarted", c => c.Set("difficulty", difficulty));
        /// <summary>The run escalated into a new wave. ctx: wave (number).</summary>
        public void NotifyChaosWaveEscalated(int wave) => Raise("ChaosWaveEscalated", c => c.Set("wave", (double)wave));
        /// <summary>A live bubble was defused in time. ctx: combo, payload (variant id), difficulty.</summary>
        public void NotifyChaosBubbleDefused(int combo, string payload, string difficulty) =>
            Raise("ChaosBubbleDefused", c => c.Set("combo", (double)combo).Set("payload", payload).Set("difficulty", difficulty));
        /// <summary>An UNSHIELDED live bubble detonated and fired its payload (real hit). ctx: payload (variant id),
        /// strength, shield_absorbed(false), run_detonations, combo (captured before the break), difficulty.</summary>
        public void NotifyChaosBubbleDetonated(string payload, double strength, double runDetonations, int combo, string difficulty) =>
            Raise("ChaosBubbleDetonated", c => c
                .Set("payload", payload).Set("strength", strength).Set("shield_absorbed", false)
                .Set("run_detonations", runDetonations).Set("combo", (double)combo).Set("difficulty", difficulty));
        /// <summary>A clutch shield-save: a live bubble's detonation was absorbed by a shield. ctx: payload (variant id),
        /// strength, shield_absorbed(true), run_detonations, combo, difficulty, shields_left.</summary>
        public void NotifyChaosBubbleDetonatedAbsorbed(string payload, double strength, double runDetonations, int combo, string difficulty, int shieldsLeft) =>
            Raise("ChaosBubbleDetonatedAbsorbed", c => c
                .Set("payload", payload).Set("strength", strength).Set("shield_absorbed", true)
                .Set("run_detonations", runDetonations).Set("combo", (double)combo).Set("difficulty", difficulty)
                .Set("shields_left", (double)shieldsLeft));
        /// <summary>A benign treat bubble was popped. ctx: variant_id, payload (display name), combo.</summary>
        public void NotifyChaosBenignPopped(string variantId, string payload, int combo) =>
            Raise("ChaosBenignPopped", c => c.Set("variant_id", variantId).Set("payload", payload).Set("combo", (double)combo));
        /// <summary>A boon was drafted. ctx: boon (name).</summary>
        public void NotifyChaosBoonPicked(string boon) => Raise("ChaosBoonPicked", c => c.Set("boon", boon));
        /// <summary>A curse was drafted (fired instead of ChaosBoonPicked for curses). ctx: boon (name), rarity, run_mult_bonus.</summary>
        public void NotifyChaosCursePicked(string boon, string rarity, double runMultBonus) =>
            Raise("ChaosCursePicked", c => c.Set("boon", boon).Set("rarity", rarity).Set("run_mult_bonus", runMultBonus));
        /// <summary>The boon draft was skipped (null pick, +1 shield). ctx: shields_now.</summary>
        public void NotifyChaosBoonSkipped(int shieldsNow) => Raise("ChaosBoonSkipped", c => c.Set("shields_now", (double)shieldsNow));
        /// <summary>A darter was caught. ctx: points, combo, quick (true = fast-catch bonus).</summary>
        public void NotifyChaosDarterCaught(double points, int combo, bool quick) =>
            Raise("ChaosDarterCaught", c => c.Set("points", points).Set("combo", (double)combo).Set("quick", quick));
        /// <summary>A freeze bubble was caught. ctx: points, combo.</summary>
        public void NotifyChaosFreezeCaught(double points, int combo) =>
            Raise("ChaosFreezeCaught", c => c.Set("points", points).Set("combo", (double)combo));
        /// <summary>A combo milestone (every 10). ctx: combo, difficulty.</summary>
        public void NotifyChaosComboMilestone(int combo, string difficulty) =>
            Raise("ChaosComboMilestone", c => c.Set("combo", (double)combo).Set("difficulty", difficulty));
        /// <summary>A high combo threshold was crossed (edge-detected). ctx: combo, threshold.</summary>
        public void NotifyChaosComboBig(int combo, double threshold) =>
            Raise("ChaosComboBig", c => c.Set("combo", (double)combo).Set("threshold", threshold));
        /// <summary>The act advanced (edge-detected). ctx: act, wave.</summary>
        public void NotifyChaosActChanged(int act, int wave) =>
            Raise("ChaosActChanged", c => c.Set("act", (double)act).Set("wave", (double)wave));
        /// <summary>The field was cleared at a wave boundary. ctx: wave (the wave just cleared).</summary>
        public void NotifyChaosWaveCleared(int wave) => Raise("ChaosWaveCleared", c => c.Set("wave", (double)wave));
        /// <summary>T-minus ~10s of a chaos run: the hole is closing (once per run, service-gated).</summary>
        public void NotifyChaosEndingSoon() => Raise("ChaosEndingSoon");
        /// <summary>The run finished. ctx: xp (final payout), difficulty, runs_completed, rank.</summary>
        public void NotifyChaosRunCompleted(int xp, string difficulty) =>
            Raise("ChaosRunCompleted", c => c.Set("xp", (double)xp).Set("difficulty", difficulty)
                .Set("runs_completed", (double)(Services.Chaos.ChaosMeta.State?.RunsCompleted ?? 0))
                .Set("rank", Services.Chaos.ChaosRanks.NameLower(Services.Chaos.ChaosMeta.RankIndex)));
        // ---- hold-to-defuse verb rework (2026-06-11). First-time gating lives in
        // ChaosModeService (chaos_meta.json flags); rh_focus_low is once per run there too. ----
        /// <summary>First ever completed defuse channel.</summary>
        public void NotifyChaosDefuseFirst() => Raise("ChaosDefuseFirst");
        /// <summary>First time a live triggered because focus couldn't cover the channel.</summary>
        public void NotifyChaosDefuseNoFocus() => Raise("ChaosDefuseNoFocus");
        /// <summary>First time an early release (mid-channel let-go) detonated a live.</summary>
        public void NotifyChaosDefuseRelease() => Raise("ChaosDefuseRelease");
        /// <summary>First time a plain click detonated a live bubble.</summary>
        public void NotifyChaosClickDetonate() => Raise("ChaosClickDetonate");
        /// <summary>Focus sat below a defuse's cost for 8s+ while lives were on screen.</summary>
        public void NotifyChaosFocusLow() => Raise("ChaosFocusLow");
        /// <summary>The Tease's first-ever appearance (debut spawn).</summary>
        public void NotifyChaosTeaseDebut() => Raise("ChaosTeaseDebut");
        /// <summary>A Tease expired untouched — the DENIED bonus paid. ctx: denied_count (this run).</summary>
        public void NotifyChaosTeaseDenied(int deniedCount) =>
            Raise("ChaosTeaseDenied", c => c.Set("denied_count", (double)deniedCount));
        /// <summary>The player touched a Tease — payload + streak halve. </summary>
        public void NotifyChaosTeaseClicked() => Raise("ChaosTeaseClicked");
        /// <summary>5+ Teases denied in a single run. ctx: denied_count.</summary>
        public void NotifyChaosTeaseDeniedStreak(int deniedCount) =>
            Raise("ChaosTeaseDeniedStreak", c => c.Set("denied_count", (double)deniedCount));
        /// <summary>The results screen was shown. ctx: score, best_score, pb_delta, is_pb, defused, detonated, best_combo, difficulty.</summary>
        public void NotifyChaosResultsShown(double score, double bestScore, double pbDelta, bool isPb,
                                            double defused, double detonated, int bestCombo, string difficulty) =>
            Raise("ChaosResultsShown", c => c
                .Set("score", score).Set("best_score", bestScore).Set("pb_delta", pbDelta).Set("is_pb", isPb)
                .Set("defused", defused).Set("detonated", detonated).Set("best_combo", (double)bestCombo)
                .Set("difficulty", difficulty)
                .Set("runs_completed", (double)(Services.Chaos.ChaosMeta.State?.RunsCompleted ?? 0))
                .Set("rank", Services.Chaos.ChaosRanks.NameLower(Services.Chaos.ChaosMeta.RankIndex)));

        // ---- Down the Rabbit Hole progression triggers (2026-06-11) ----
        /// <summary>Rank increased on run completion. ctx: rank (lowercase word).</summary>
        public void NotifyChaosRankUp(string rank) => Raise("ChaosRankUp", c => c.Set("rank", rank));
        /// <summary>A pending reveal flashed on dollhouse open (once per batch). ctx: element (reveal id).</summary>
        public void NotifyChaosRevealFlash(string element) => Raise("ChaosRevealFlash", c => c.Set("element", element));
        /// <summary>A lesson completed. ctx: lesson_id.</summary>
        public void NotifyChaosLessonComplete(string lessonId) => Raise("ChaosLessonComplete", c => c.Set("lesson_id", lessonId));
        /// <summary>She auto-covered a short balance on the first toy pocket attempt (one-time).</summary>
        public void NotifyChaosGiftGiven() => Raise("ChaosGiftGiven");
        /// <summary>A one-time first-times bonus awarded. ctx: bonus_id.</summary>
        public void NotifyChaosFirstTime(string bonusId) => Raise("ChaosFirstTime", c => c.Set("bonus_id", bonusId));
        /// <summary>First run with both e_stim + the_spanker: the scripted duo draft fired.</summary>
        public void NotifyChaosDuoDemo() => Raise("ChaosDuoDemo");
        /// <summary>First gold income ever (gold explained).</summary>
        public void NotifyChaosGoldFirst() => Raise("ChaosGoldFirst");
        /// <summary>The dollhouse opened for the very first time.</summary>
        public void NotifyChaosDollhouseFirstOpen() => Raise("ChaosDollhouseFirstOpen");
        /// <summary>An untouched draft timed out before the skip is revealed: it autopicked a card.</summary>
        public void NotifyChaosDraftAutopick() => Raise("ChaosDraftAutopick");

        /// <summary>
        /// Reload rules from disk (e.g. after a mod switch so the new mod's manifest applies).
        /// Rotation / cooldown / session one-shot state is keyed by rule id, and ids are SHARED
        /// across mods (same id → different content), so it is cleared here: otherwise a line
        /// fired under the old mod would suppress the same-id line under the new one. Persisted
        /// lifetime/tier latches (AppSettings) are intentionally NOT cleared — those are once-ever.
        /// </summary>
        public void ReloadRules()
        {
            try
            {
                var fresh = BarkRuleLoader.Load();
                _engine.Reload(fresh); // re-arms session one-shots and cooldowns; rotation is kept
                App.Logger?.Information("BarkService: reloaded {Count} rules (rotation preserved)", fresh.Count);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: rule reload failed"); }
        }

        // ===================== subscription block =====================

        private void WireSubscriptions()
        {
            // ---- Webcam / gaze ----
            if (App.Webcam != null)
            {
                Wire<Action>(h => App.Webcam.OnBlink += h, h => App.Webcam.OnBlink -= h,
                    () => { _state.RegisterBlink(); Raise("Blink", c => c.Set("blink_count", _state.BlinkCount)); });
                Wire<Action>(h => App.Webcam.OnMouthOpen += h, h => App.Webcam.OnMouthOpen -= h,
                    () => Raise("MouthOpen"));
                Wire<Action>(h => App.Webcam.OnTongueOut += h, h => App.Webcam.OnTongueOut -= h,
                    () => Raise("TongueOut"));
                Wire<Action<System.Windows.Point>>(h => App.Webcam.OnLongStare += h, h => App.Webcam.OnLongStare -= h,
                    _ => Raise("LongStare"));
                Wire<Action>(h => App.Webcam.OnFaceLost += h, h => App.Webcam.OnFaceLost -= h,
                    () => { _state.FaceLost(); Raise("FaceLost"); });
                // Raise BEFORE clearing so face_lost_sec still reads the elapsed lost-duration
                // (enables the prolonged-FaceLost egg's distinct very-long threshold).
                Wire<Action>(h => App.Webcam.OnFaceFound += h, h => App.Webcam.OnFaceFound -= h,
                    () => { Raise("FaceFound"); _state.FaceFound(); });
                Wire<Action<WebcamTrackingState>>(h => App.Webcam.OnTrackingStateChanged += h, h => App.Webcam.OnTrackingStateChanged -= h,
                    s => Raise("TrackingStateChanged", c => c.Set("state", s.ToString())));
            }

            // ---- Gaze focus ----
            if (App.GazeFocus != null)
            {
                Wire<Action>(h => App.GazeFocus.GazePopped += h, h => App.GazeFocus.GazePopped -= h,
                    () => Raise("GazePopped"));
                Wire<Action<bool>>(h => App.GazeFocus.OnActiveChanged += h, h => App.GazeFocus.OnActiveChanged -= h,
                    active => Raise("GazeActiveChanged", c => c.Set("active", active)));
            }

            // ---- Blink trainer ----
            if (App.BlinkTrainer != null)
                Wire<Action>(h => App.BlinkTrainer.StateChanged += h, h => App.BlinkTrainer.StateChanged -= h,
                    () => Raise("BlinkTrainerStateChanged", c => c.Set("running", App.BlinkTrainer?.IsRunning ?? false)));

            // ---- Video ----
            if (App.Video != null)
            {
                Wire<EventHandler>(h => App.Video.VideoAboutToStart += h, h => App.Video.VideoAboutToStart -= h,
                    (_, __) => Raise("VideoAboutToStart"));
                Wire<EventHandler>(h => App.Video.VideoStarted += h, h => App.Video.VideoStarted -= h,
                    (_, __) => Raise("VideoStarted"));
                Wire<EventHandler>(h => App.Video.VideoEnded += h, h => App.Video.VideoEnded -= h,
                    (_, __) => Raise("VideoEnded"));
            }

            // ---- Attention checks (GATED on a video actually playing) ----
            if (App.AttentionCheck != null)
            {
                Wire<Action>(h => App.AttentionCheck.OnPass += h, h => App.AttentionCheck.OnPass -= h,
                    () => { if (App.Video?.IsPlaying == true) Raise("AttentionCheckPass", c => c.Set("video_playing", true)); });
                Wire<Action>(h => App.AttentionCheck.OnFail += h, h => App.AttentionCheck.OnFail -= h,
                    () => { if (App.Video?.IsPlaying == true) Raise("AttentionCheckFail", c => c
                        .Set("video_playing", true)
                        .Set("fail_count", App.Video?.PlaythroughFailCount ?? 0)); });
            }

            // ---- Bubble minigames ----
            if (App.Bubbles != null)
            {
                Wire<Action>(h => App.Bubbles.OnBubblePopped += h, h => App.Bubbles.OnBubblePopped -= h,
                    () => Raise("BubblePopped"));
                Wire<Action>(h => App.Bubbles.OnBubbleMissed += h, h => App.Bubbles.OnBubbleMissed -= h,
                    () => Raise("BubbleMissed"));
            }
            if (App.BubbleCount != null)
            {
                Wire<EventHandler>(h => App.BubbleCount.GameCompleted += h, h => App.BubbleCount.GameCompleted -= h,
                    (_, __) => Raise("BubbleCountCompleted"));
                Wire<EventHandler>(h => App.BubbleCount.GameFailed += h, h => App.BubbleCount.GameFailed -= h,
                    (_, __) => Raise("BubbleCountFailed"));
            }
            if (App.BouncingText != null)
            {
                Wire<EventHandler>(h => App.BouncingText.OnBounce += h, h => App.BouncingText.OnBounce -= h,
                    (_, __) => Raise("BouncingTextBounce"));
                // DVD corner hit (true/near corner) — distinct from a plain wall bounce.
                Wire<EventHandler>(h => App.BouncingText.OnCornerHit += h, h => App.BouncingText.OnCornerHit -= h,
                    (_, __) => Raise("BouncingTextCorner"));
            }

            // ---- Lock card (sole subscriber; Fork-D 50/50 coin flip in HandleLockCardCompleted) ----
            if (App.LockCard != null)
                Wire<EventHandler<LockCardCompletedEventArgs>>(
                    h => App.LockCard.LockCardCompleted += h, h => App.LockCard.LockCardCompleted -= h,
                    (_, e) => HandleLockCardCompleted(e));

            // ---- Visual fx ----
            if (App.Flash != null)
                Wire<EventHandler>(h => App.Flash.FlashDisplayed += h, h => App.Flash.FlashDisplayed -= h,
                    (_, __) => Raise("FlashDisplayed"));
            if (App.Subliminal != null)
                Wire<EventHandler>(h => App.Subliminal.SubliminalDisplayed += h, h => App.Subliminal.SubliminalDisplayed -= h,
                    (_, __) => Raise("SubliminalDisplayed"));
            if (App.BrainDrain != null)
                Wire<EventHandler>(h => App.BrainDrain.BrainDrainTriggered += h, h => App.BrainDrain.BrainDrainTriggered -= h,
                    (_, __) => Raise("BrainDrainTriggered"));
            if (App.MindWipe != null)
                Wire<EventHandler>(h => App.MindWipe.MindWipeTriggered += h, h => App.MindWipe.MindWipeTriggered -= h,
                    (_, __) => Raise("MindWipeTriggered"));

            // ---- Awareness / keywords ----
            if (App.KeywordTriggers != null)
                Wire<EventHandler<KeywordTrigger>>(h => App.KeywordTriggers.TriggerFired += h, h => App.KeywordTriggers.TriggerFired -= h,
                    (_, kt) => Raise("KeywordTriggerFired", c => c
                        .Set("keyword", kt?.Keyword ?? "")
                        .Set("kw_effect", kt?.VisualEffect.ToString() ?? "")));
            if (App.WindowAwareness != null)
            {
                // Awareness v2: when the arbiter is driving, these two must NOT fire. They are one of
                // the two legacy mouths (doc 02 §1.5) — the other is AvatarTubeWindow's reaction
                // handlers — and both firing on one window change is the double-reaction bug. The
                // arbiter re-raises the same triggers through RaiseAwarenessBark once it has decided
                // the moment is worth a bark, so nothing is lost, it is just gated. With v2 off (or
                // unwired) this is byte-for-byte today's behaviour.
                Wire<EventHandler<ActivityChangedEventArgs>>(h => App.WindowAwareness.ActivityChanged += h, h => App.WindowAwareness.ActivityChanged -= h,
                    (_, a) =>
                    {
                        if (Awareness.AwarenessV2Routing.IsActive) return;
                        Raise("ActivityChanged", c => c
                            .Set("activity", a?.ServiceName ?? "")
                            .Set("category", a?.Category.ToString() ?? "")
                            // Fine-grained awareness (item G) — populated only when AppClusterMap matched the
                            // title; empty otherwise so app_cluster_eq/app_eq rules simply don't match.
                            .Set("app_cluster", a?.AppCluster ?? "")
                            .Set("app", a?.AppId ?? ""));
                    });
                Wire<EventHandler<ActivityChangedEventArgs>>(h => App.WindowAwareness.StillOnActivity += h, h => App.WindowAwareness.StillOnActivity -= h,
                    (_, a) =>
                    {
                        if (Awareness.AwarenessV2Routing.IsActive) return;
                        Raise("StillOnActivity", c => c
                            .Set("activity", a?.ServiceName ?? "")
                            .Set("still_minutes", App.WindowAwareness?.CurrentActivityDuration.TotalMinutes ?? 0));
                    });
            }

            // ---- Progression / companion / skills ----
            if (App.Progression != null)
            {
                Wire<EventHandler<int>>(h => App.Progression.LevelUp += h, h => App.Progression.LevelUp -= h,
                    (_, lvl) => Raise("LevelUp", c => c.Set("level", lvl)));
                Wire<EventHandler<double>>(h => App.Progression.XPChanged += h, h => App.Progression.XPChanged -= h,
                    (_, xp) => Raise("XPChanged", c => c.Set("xp", xp)));
            }
            if (App.Companion != null)
            {
                Wire<EventHandler<(CompanionId Companion, int NewLevel)>>(
                    h => App.Companion.CompanionLevelUp += h, h => App.Companion.CompanionLevelUp -= h,
                    (_, e) => Raise("CompanionLevelUp", c => c.Set("level", e.NewLevel)));
                Wire<EventHandler>(h => App.Companion.UserMessageSent += h, h => App.Companion.UserMessageSent -= h,
                    (_, __) => { _engine.NoteUserMessage(); Raise("UserMessageSent"); });
            }
            if (App.SkillTree != null)
            {
                Wire<EventHandler<string>>(h => App.SkillTree.SkillUnlocked += h, h => App.SkillTree.SkillUnlocked -= h,
                    (_, id) => Raise("SkillUnlocked", c => c.Set("skill", id ?? "")));
                Wire<EventHandler<LuckyProcEventArgs>>(h => App.SkillTree.LuckyProc += h, h => App.SkillTree.LuckyProc -= h,
                    (_, __) => Raise("LuckyProc"));
                Wire<EventHandler>(h => App.SkillTree.PinkRushStarted += h, h => App.SkillTree.PinkRushStarted -= h,
                    (_, __) => Raise("PinkRushStarted"));
                Wire<EventHandler>(h => App.SkillTree.PinkRushEnded += h, h => App.SkillTree.PinkRushEnded -= h,
                    (_, __) => Raise("PinkRushEnded"));
            }

            // ---- Achievements / roadmap / quests / quiz / mantra ----
            if (App.Achievements != null)
                Wire<EventHandler<Achievement>>(h => App.Achievements.AchievementUnlocked += h, h => App.Achievements.AchievementUnlocked -= h,
                    (_, a) => Raise("AchievementUnlocked", c => c.Set("achievement", a?.Id ?? "")));
            if (App.Roadmap != null)
            {
                Wire<EventHandler<RoadmapStepCompletedEventArgs>>(h => App.Roadmap.StepCompleted += h, h => App.Roadmap.StepCompleted -= h,
                    (_, __) => Raise("RoadmapStepCompleted"));
                Wire<EventHandler<RoadmapTrack>>(h => App.Roadmap.TrackUnlocked += h, h => App.Roadmap.TrackUnlocked -= h,
                    (_, __) => Raise("RoadmapTrackUnlocked"));
                Wire<EventHandler>(h => App.Roadmap.BadgeEarned += h, h => App.Roadmap.BadgeEarned -= h,
                    (_, __) => Raise("RoadmapBadgeEarned"));
            }
            if (App.Quests != null)
            {
                Wire<EventHandler<QuestCompletedEventArgs>>(h => App.Quests.QuestCompleted += h, h => App.Quests.QuestCompleted -= h,
                    (_, __) => Raise("QuestCompleted"));
                Wire<EventHandler<QuestProgressEventArgs>>(h => App.Quests.QuestProgressChanged += h, h => App.Quests.QuestProgressChanged -= h,
                    (_, __) => Raise("QuestProgressChanged"));
                Wire<EventHandler>(h => App.Quests.QuestsRefreshed += h, h => App.Quests.QuestsRefreshed -= h,
                    (_, __) => Raise("QuestsRefreshed"));
            }
            // QuizCompleted is a STATIC event — must unsubscribe in Stop() to avoid a dangling handler.
            {
                EventHandler<QuizCompletedEventArgs> h = (_, e) =>
                {
                    // TWO gates, both deliberate. The `quiz_done` pool is written for a run that
                    // went WELL ("passed the quiz", "good girl, all correct", "aced it") and every
                    // built-in mod declares it with cooldown 0, repeatable, and NO conditions — so
                    // the only place the content can be matched to the outcome is here, in C#. Mod
                    // manifests are content and third-party mods can't be patched, so the gate has
                    // to live in the subscription layer.
                    //
                    // 1. CONTENT — since #870 the graded intake is a live raiser, and it reports
                    //    passed:true for EVERY finished run (an intake has no fail state). Without
                    //    a gate a 30%-compliance run gets told it aced it. Speak only for a run
                    //    that genuinely earned the line: `perfect` (both surfaces set it at the
                    //    same 90% top-marks bar) or a real pass — from the classic quiz `passed`
                    //    IS the honest >= 60% bar it has always used.
                    // 2. TIMING — while the intake host window is up the run is mid-outro, its own
                    //    voiced ceremony. A bark there talks over it. Stay quiet and let the
                    //    ceremony land; the run is still credited (the bridge's own subscription
                    //    is separate and unaffected).
                    //
                    // Net effect: quiz_done is classic-quiz-only, which is what it was before #870
                    // wired the intake into the same event.
                    if (!e.Perfect && !e.Passed) return;
                    if (Quiz.IntakeHostService.IsActive) return;
                    // Same reasoning for the Arcademy: it is a modal WebView2 window with its own
                    // voiced/animated ceremonies, and MainWindow is tucked to the tray behind it.
                    if (Arcademy.ArcademyHostService.IsActive) return;
                    Raise("QuizCompleted", c => c.Set("passed", e.Passed).Set("perfect", e.Perfect));
                };
                QuizService.QuizCompleted += h;
                _unsubscribe.Add(() => QuizService.QuizCompleted -= h);
            }
            if (App.Mantra != null)
            {
                Wire<Action>(h => App.Mantra.MantraCompleted += h, h => App.Mantra.MantraCompleted -= h,
                    () => Raise("MantraCompleted"));
                Wire<Action<int>>(h => App.Mantra.StreakChanged += h, h => App.Mantra.StreakChanged -= h,
                    streak => Raise("MantraStreakChanged", c => c.Set("streak", streak)));
                Wire<Action>(h => App.Mantra.StreakBroken += h, h => App.Mantra.StreakBroken -= h,
                    () => Raise("MantraStreakBroken"));
            }

            // ---- Control / system ----
            if (App.Lockdown != null)
            {
                Wire<Action>(h => App.Lockdown.LockdownActivated += h, h => App.Lockdown.LockdownActivated -= h,
                    () => Raise("LockdownActivated"));
                Wire<Action>(h => App.Lockdown.LockdownDeactivated += h, h => App.Lockdown.LockdownDeactivated -= h,
                    () => Raise("LockdownDeactivated"));
                Wire<Action<TimeSpan>>(h => App.Lockdown.CountdownTick += h, h => App.Lockdown.CountdownTick -= h,
                    ts => Raise("LockdownCountdownTick", c => c.Set("remaining_sec", ts.TotalSeconds)));
            }
            if (App.RemoteControl != null)
            {
                Wire<EventHandler<string>>(h => App.RemoteControl.CommandReceived += h, h => App.RemoteControl.CommandReceived -= h,
                    (_, action) => Raise("RemoteCommandReceived", c => c.Set("command", action ?? "")));
                Wire<EventHandler>(h => App.RemoteControl.ControllerConnectedChanged += h, h => App.RemoteControl.ControllerConnectedChanged -= h,
                    (_, __) => Raise("ControllerConnectedChanged"));
            }
            if (App.Mods != null)
                Wire<EventHandler<ModPackage>>(h => App.Mods.ModChanged += h, h => App.Mods.ModChanged -= h,
                    (_, mod) =>
                    {
                        _state.RegisterModSwitch();
                        // The new mod ships its own bark manifest — reload so its lines/voicelines
                        // apply. ModChanged fires after ModService sets the active mod, so the
                        // loader reads the new ActiveModId. (Raise the switch bark off the OLD set
                        // first, since the new set may not define a ModChanged rule.)
                        Raise("ModChanged", c => c
                            .Set("mod", mod?.Id ?? "")
                            .Set("mod_switches_60s", _state.ModSwitchesWithin(RapidModSwitchWindow)));
                        ReloadRules();
                    });
            if (App.ActivityTracker != null)
                Wire<EventHandler<bool>>(h => App.ActivityTracker.IdleStateChanged += h, h => App.ActivityTracker.IdleStateChanged -= h,
                    (_, idle) => Raise("IdleStateChanged", c => c.Set("idle", idle)));
            if (App.Update != null)
                Wire<EventHandler<UpdateInfo>>(h => App.Update.UpdateAvailable += h, h => App.Update.UpdateAvailable -= h,
                    (_, __) => Raise("UpdateAvailable"));
            if (App.Patreon != null)
                Wire<EventHandler<PatreonTier>>(h => App.Patreon.TierChanged += h, h => App.Patreon.TierChanged -= h,
                    (_, tier) =>
                    {
                        // An invite week (and its end) is a free trial, not a purchase or a lapse:
                        // no "you gave me money" egg, and _lastTier stays put so the friend's real
                        // subscription later still reads as the tier-up it is.
                        if (App.Patreon?.IsInviteWeekOnly == true) return;
                        if (tier == PatreonTier.None && _lastTier == PatreonTier.None) return;
                        bool up = tier > _lastTier; // enum is ordinal (None < Level1 < …)
                        _lastTier = tier;
                        Raise("PatreonTierChanged", c => c.Set("tier", tier.ToString()).Set("tier_up", up));
                    });
            if (App.Discord != null)
                Wire<EventHandler<bool>>(h => App.Discord.AuthenticationChanged += h, h => App.Discord.AuthenticationChanged -= h,
                    (_, ok) => Raise("DiscordAuthChanged", c => c.Set("authenticated", ok)));
            if (App.Tutorial != null)
                Wire<EventHandler>(h => App.Tutorial.TutorialCompleted += h, h => App.Tutorial.TutorialCompleted -= h,
                    (_, __) => Raise("TutorialCompleted"));

            // LocalAiService.PersistentMemoryRecalled is a STATIC event — unsubscribe in Stop().
            {
                EventHandler h = (_, __) => Raise("PersistentMemoryRecalled");
                LocalAiService.PersistentMemoryRecalled += h;
                _unsubscribe.Add(() => LocalAiService.PersistentMemoryRecalled -= h);
            }
        }

        /// <summary>
        /// Wire SessionEngine events. SessionEngine is MainWindow-owned and created lazily on
        /// the first session, so MainWindow calls this once it constructs the engine. Re-attach
        /// safely detaches any previous engine wiring.
        /// </summary>
        public void AttachSessionEngine(SessionEngine engine)
        {
            if (engine == null) return;
            try
            {
                RunUnsubscribers(_engineUnsubscribe);
                _state.AttachSessionEngine(engine);

                EventHandler started = (_, __) =>
                {
                    // A session started — the setup screen is gone, so cancel any pending setup-ready nudge.
                    try { _setupReadyTimer?.Stop(); } catch { }
                    _setupReadyStallPending = false;
                    _state.ResetSessionScoped();
                    Raise("SessionStarted");
                };
                EventHandler stopped = (_, __) => Raise("SessionStopped");
                EventHandler<SessionCompletedEventArgs> completed = (_, __) => Raise("SessionCompleted");
                EventHandler<SessionPhaseChangedEventArgs> phase = (_, e) =>
                {
                    _state.SetPhase(e?.Phase?.Name);
                    Raise("SessionPhaseChanged", c => c
                        .Set("phase_index", e?.PhaseIndex ?? -1)
                        .Set("phase_name", e?.Phase?.Name ?? "")
                        .Set("phase_is_deepener", _state.CurrentPhaseIsDeepener));
                };
                // Periodic in-session tick — drives time-threshold eggs (e.g. marathon: session_elapsed_sec >= 3h).
                EventHandler<SessionProgressEventArgs> progress = (_, __) =>
                    Raise("SessionProgress", c => c.Set("session_elapsed_sec", _state.SessionElapsedSeconds));

                // Pause / resume: the "you stopped, and you came back" family. pause_n is the
                // 1-based pause count this session; paused_sec is how long the resume waited.
                EventHandler<int> paused = (_, n) =>
                    Raise("SessionPaused", c => c.Set("pause_n", (double)n));
                EventHandler<TimeSpan> resumed = (_, gap) =>
                    Raise("SessionResumed", c => c
                        .Set("pause_n", (double)_state.PauseCount)
                        .Set("paused_sec", gap.TotalSeconds));

                engine.SessionStarted += started;
                engine.SessionStopped += stopped;
                engine.SessionCompleted += completed;
                engine.PhaseChanged += phase;
                engine.ProgressUpdated += progress;
                engine.SessionPaused += paused;
                engine.SessionResumed += resumed;

                _engineUnsubscribe.Add(() => engine.SessionStarted -= started);
                _engineUnsubscribe.Add(() => engine.SessionStopped -= stopped);
                _engineUnsubscribe.Add(() => engine.SessionCompleted -= completed);
                _engineUnsubscribe.Add(() => engine.PhaseChanged -= phase);
                _engineUnsubscribe.Add(() => engine.ProgressUpdated -= progress);
                _engineUnsubscribe.Add(() => engine.SessionPaused -= paused);
                _engineUnsubscribe.Add(() => engine.SessionResumed -= resumed);

                AttachMainWindowFocus();

                App.Logger?.Debug("BarkService: attached to SessionEngine");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AttachSessionEngine failed"); }
        }

        /// <summary>
        /// Wire the main window's focus so a session that loses the window and gets it back
        /// raises <c>SessionRefocused</c> (away_sec, refocus_n). The window exists by the time the
        /// session engine is attached (it is MainWindow-owned), which is why this hangs off
        /// <see cref="AttachSessionEngine"/> rather than <see cref="Start"/>. Unsubscribed with the
        /// engine wiring, so a re-attach never double-subscribes.
        /// </summary>
        private void AttachMainWindowFocus()
        {
            try
            {
                var window = System.Windows.Application.Current?.MainWindow;
                if (window == null) return;

                EventHandler deactivated = (_, __) =>
                {
                    if (_state.SessionRunning) _state.RegisterUnfocus();
                };
                EventHandler activated = (_, __) =>
                {
                    if (!_state.SessionRunning) return;
                    var away = _state.RegisterRefocus();
                    if (away < 0) return;
                    Raise("SessionRefocused", c => c
                        .Set("away_sec", away)
                        .Set("refocus_n", (double)_state.RefocusCount));
                };

                window.Deactivated += deactivated;
                window.Activated += activated;
                _engineUnsubscribe.Add(() => window.Deactivated -= deactivated);
                _engineUnsubscribe.Add(() => window.Activated -= activated);
            }
            catch (Exception ex) { App.Logger?.Debug(ex, "BarkService: main window focus wiring skipped"); }
        }

        /// <summary>
        /// Wire the MainWindow-owned tray icon (created in MainWindow's constructor).
        /// Re-attach safe: detaches any previous tray wiring before subscribing, so a
        /// repeat call can't double-subscribe (no double-fire, no leak).
        /// </summary>
        public void AttachTray(TrayIconService tray)
        {
            if (tray == null) return;
            try
            {
                RunUnsubscribers(_trayUnsubscribe);

                Action wake = () => Raise("WakeBambiRequested");
                tray.OnWakeBambiRequested += wake;
                _trayUnsubscribe.Add(() => tray.OnWakeBambiRequested -= wake);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AttachTray failed"); }
        }

        // ===================== subscription helper =====================

        private void Wire<TDelegate>(Action<TDelegate> subscribe, Action<TDelegate> unsubscribe, TDelegate handler)
            where TDelegate : Delegate
        {
            try
            {
                subscribe(handler);
                _unsubscribe.Add(() => unsubscribe(handler));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkService: failed to wire a subscription");
            }
        }

        // ===================== matcher =====================

        /// <param name="guaranteed">
        /// When true the gate is bypassed (cooldown / min-gap / one-fire / chat-suppression /
        /// safety-hold) and a variant is always selected — used for direct reactions that must
        /// fire exactly once (e.g. the lock-card pool bark on a tails coin flip).
        /// </param>
        /// <returns>True if a bark was actually spoken (rule matched + gate passed + not dry-run).</returns>
        private bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
        {
            try
            {
                // EMI Desk (MOMENTS 4.A): mirror the trigger onto her moment bus BEFORE the rule
                // lookup below, which early-returns for any trigger no bark rule is keyed to - and
                // most of the events she cares about have no rule in the shipped bark_rules.json.
                // The mirror builds its own context, swallows everything, and is a no-op while she
                // is away, so it can neither change nor delay the bark under it.
                Services.EmiDesk.EmiBarkBridge.Mirror(trigger, fill);

                if (_engine.Rules.ForTrigger(trigger).Count == 0) return false;

                var ctx = new BarkContext(trigger);
                fill?.Invoke(ctx);

                // Core decides (priority walk, gate, commit) under its lock; render after it returns
                // (GigglePriority/Giggle marshal to UI).
                var pick = _engine.Decide(trigger, ctx, guaranteed);
                if (pick == null) return false;
                RecordWithArbiter(pick.Rule);
                Speak(pick);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkService: Raise('{Trigger}') failed", trigger);
                return false;
            }
        }

        /// <summary>
        /// One mouth, in BOTH directions (doc 02 §5.1). The arbiter's cooldown ledger is the shared
        /// "when did anything last speak", so every non-safety bark this service fires on its own —
        /// achievement, level-up, video-done, flash, idle chatter — has to land in it. Safety barks are
        /// exempt for the same reason they bypass every other floor. The awareness bark path is excluded
        /// because the arbiter records that one itself (recording twice double-charges the hourly
        /// budget). IsActive, not "an arbiter exists": with the v2 kill switch down no v2 state may
        /// have any effect. Only called for a committed, non-dry-run fire.
        /// </summary>
        private void RecordWithArbiter(BarkRule rule)
        {
            if (_inAwarenessBark || rule.Class == BarkClass.Safety || !Awareness.AwarenessV2Routing.IsActive) return;
            try { Awareness.AwarenessV2Routing.Arbiter?.RecordExternalLine(Awareness.ReactionSource.Bark); }
            catch (Exception ex) { App.Logger?.Debug("BarkService: arbiter report failed: {Error}", ex.Message); }
        }

        // ===================== idle chatter (item F) =====================

        /// <summary>
        /// Raise an idle ("Idle") bark, chosen with a pool-wide no-repeat across ALL eligible idle
        /// rules (Core <see cref="BarkEngine.DecideIdle"/>). Called by AvatarTubeWindow's idle timer
        /// (companion-tab slider cadence). Skipped while muted or while she's already speaking so idle
        /// never talks over a real bark; the normal gate still applies on top.
        /// </summary>
        public void DispatchIdle()
        {
            try
            {
                // Pause idle while muted or already speaking — real barks always win.
                if ((App.Settings?.Current?.MasterVolume ?? 0) == 0) return;
                if (App.AvatarWindow?.IsSpeaking ?? false) return;

                var pick = _engine.DecideIdle();
                if (pick == null) return;
                RecordWithArbiter(pick.Rule);
                Speak(pick);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: DispatchIdle failed"); }
        }

        // ===================== anticipatory setup-ready detector (item E) =====================

        /// <summary>
        /// (Re)start the SessionSetupReady debounce after a setup action (setting change). Fires when the
        /// user goes quiet (~8–12s after the LAST action), not on every click. No-op while a session is
        /// running. Marshals to the UI thread since the settings setter may call us off-thread.
        /// </summary>
        private void ArmSetupReadyDebounce()
        {
            if (_state.SessionRunning) return;
            DispatcherHelper.RunOnUI(() =>
            {
                if (!_started) return;
                _setupReadyStallPending = false;
                if (_setupReadyTimer == null)
                {
                    _setupReadyTimer = new System.Windows.Threading.DispatcherTimer();
                    _setupReadyTimer.Tick += OnSetupReadyTick;
                }
                _setupReadyTimer.Stop();
                _setupReadyTimer.Interval = TimeSpan.FromSeconds(
                    _rng.Next(SetupReadyDebounceMinSec, SetupReadyDebounceMaxSec + 1));
                _setupReadyTimer.Start();
            });
        }

        private void OnSetupReadyTick(object? sender, EventArgs e)
        {
            _setupReadyTimer?.Stop();
            try
            {
                if (!SetupConditionsMet()) { _setupReadyStallPending = false; return; }

                Raise("SessionSetupReady");

                // setup_ready_stall needs ~45s of idle, but this first fire lands at ~10s. If the user is
                // still settling, schedule ONE more raise once the stall window elapses (the per-rule
                // scope:session latches keep each line one-shot, so the earlier ready line won't repeat).
                if (!_setupReadyStallPending && _state.SetupIdleSeconds < SetupReadyStallSec && _setupReadyTimer != null)
                {
                    _setupReadyStallPending = true;
                    double wait = Math.Max(5, SetupReadyStallSec - _state.SetupIdleSeconds + 2);
                    _setupReadyTimer.Interval = TimeSpan.FromSeconds(wait);
                    _setupReadyTimer.Start();
                }
                else
                {
                    _setupReadyStallPending = false;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: setup-ready tick failed"); }
        }

        /// <summary>
        /// SessionSetupReady gate: engine not running + ≥2 setting changes this app-open + at least one
        /// content feature enabled + media present on disk. (MediaAdded was cut this pass; this covers
        /// the "everything's ready, press start" moment.)
        /// </summary>
        private bool SetupConditionsMet()
        {
            var s = App.Settings?.Current;
            if (s == null || _state.SessionRunning) return false;
            if (_state.SettingsChangedThisSession < 2) return false;
            if (!(s.FlashEnabled || s.MandatoryVideosEnabled)) return false; // at least one asset enabled
            return HasAnyMedia();
        }

        /// <summary>Cheap probe: does the assets folder hold at least one image or video file?</summary>
        private static bool HasAnyMedia()
        {
            try
            {
                var root = App.EffectiveAssetsPath;
                if (string.IsNullOrEmpty(root)) return false;
                foreach (var sub in new[] { "images", "videos" })
                {
                    var dir = System.IO.Path.Combine(root, sub);
                    if (System.IO.Directory.Exists(dir) && System.IO.Directory.EnumerateFiles(dir).Any())
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        // ===================== lock card (Fork D: single coin flip, exactly one fires) =====================

        private void HandleLockCardCompleted(LockCardCompletedEventArgs e)
        {
            try
            {
                bool heads = _rng.Next(2) == 0;
                bool aiAvailable = App.AvatarWindow != null
                    && App.Settings?.Current?.AiChatEnabled == true
                    && App.Ai?.IsAvailable == true;

                if (heads && aiAvailable)
                {
                    if (DryRun)
                    {
                        App.Logger?.Information("[BARK dry-run] LockCard coin=heads -> WOULD play AI lock reaction");
                        return;
                    }
                    App.Logger?.Information("[BARK] LockCard coin=heads -> AI lock reaction");
                    _ = PlayLockReactionThenMaybePool(e);
                }
                else
                {
                    // tails, or heads-but-AI-unavailable -> guaranteed pool bark (never neither).
                    var why = heads ? "heads(ai-unavailable)" : "tails";
                    App.Logger?.Information("[BARK] LockCard coin={Why} -> pool bark", why);
                    FireLockCardPoolBark(e);
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: lock-card handler failed"); }
        }

        private async System.Threading.Tasks.Task PlayLockReactionThenMaybePool(LockCardCompletedEventArgs e)
        {
            bool spoke = false;
            try
            {
                var avatar = App.AvatarWindow;
                if (avatar != null) spoke = await avatar.PlayLockCardAiReactionAsync(e);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AI lock reaction threw"); }

            if (!spoke)
            {
                // AI produced nothing — fall through so exactly one reaction still fires.
                App.Logger?.Information("[BARK] LockCard heads but AI silent -> pool bark fallback");
                DispatcherHelper.RunOnUI(() => FireLockCardPoolBark(e));
            }
        }

        private void FireLockCardPoolBark(LockCardCompletedEventArgs e) =>
            Raise("LockCardCompleted", c => c
                .Set("phrase", e.Phrase ?? "")
                .Set("mistakes", e.Mistakes)
                .Set("repeats", e.Repeats), guaranteed: true);

        /// <summary>
        /// The condition fields only this head can read (Core <see cref="BarkEngine"/> resolves the
        /// settings/time ones and falls back to the per-fire context when this returns null).
        /// </summary>
        private object? ResolveLiveField(string field)
        {
            switch (field)
            {
                case "video_playing": return App.Video?.IsPlaying ?? false;
                // True only while webcam tracking is actually live (Tracking/FaceLost) — the same
                // source the OnFaceFound/OnBlink/OnLongStare events fire from. Lets watching/camera-
                // themed lines on non-webcam triggers (Idle, etc.) gate on the cam actually being on.
                case "webcam_running": return App.Webcam?.IsRunning ?? false;
                case "session_running": return _state.SessionRunning;
                case "setup_idle_sec": return _state.SetupIdleSeconds;
                case "session_elapsed_sec": return _state.SessionElapsedSeconds;
                case "session_phase_index": return _state.SessionPhaseIndex;
                case "blink_count": return (double)_state.BlinkCount;
                case "face_lost_sec": return _state.FaceLostSeconds;
                case "mod_switches_60s": return _state.ModSwitchesWithin(RapidModSwitchWindow);
                case "days_away": return _state.DaysAwayAtLaunch;
                case "instant_relaunch": return _state.InstantRelaunch;

                // --- lifetime-completion egg conditions ---
                case "achievements_all_unlocked":
                {
                    // THE SAME 100% THE TAB SHOWS. The unfiltered pair counted the thirteen
                    // patron-exclusives (and, until the premium rule landed, the four paid
                    // program badges) into a denominator a free user can never reach, so
                    // egg_100pct and ms_all_achievements were unfireable by construction.
                    // exclusive:false is the free collection, and GetTotalCount(false) already
                    // drops a locked premium-program badge for a user without premium - so this
                    // trips at the ceiling the player is actually shown.
                    var total = App.Achievements?.GetTotalCount(exclusive: false) ?? 0;
                    return total > 0 && (App.Achievements?.GetUnlockedCount(exclusive: false) ?? 0) >= total;
                }
                case "all_skills_unlocked":
                {
                    var total = SkillDefinition.All?.Count ?? 0;
                    return total > 0 && (App.SkillTree?.GetUnlockedSkills().Count ?? 0) >= total;
                }
                // (max-level egg uses the existing "player_level_gte" condition — there is no level cap.)

                // --- session phase (for deepener conditions on non-phase events) ---
                case "phase_name": return _state.CurrentPhaseName;
                case "phase_is_deepener": return _state.CurrentPhaseIsDeepener;

                // --- "I noticed" reads: the real-hit half of a cold read. Every one of these is a
                // fact the app actually holds; a rule that needs one and finds it at 0 should simply
                // not match, never guess. ---
                case "pauses_this_session": return (double)_state.PauseCount;
                case "refocus_this_session": return (double)_state.RefocusCount;
                case "session_planned_min": return _state.SessionPlannedMinutes;

                default: return null;
            }
        }

        /// <summary>
        /// Stable identifier for a single bark line, used to toggle it in the Phrase Manager and to
        /// gate it at speak time. Keyed off the audio filename (e.g. <c>flash_12</c>) so it survives
        /// reordering/content edits — an index-based id would silence the wrong line after a content
        /// update. Text-only variants fall back to a slug of their text. The <c>Bark:</c> prefix never
        /// collides with the manager's <c>Category:index</c> / GUID ids, so it shares
        /// <see cref="AppSettings.DisabledPhraseIds"/> / <see cref="AppSettings.RemovedPhraseIds"/>.
        /// </summary>
        public static string BarkLineId(string ruleId, BarkVariant v) =>
            CompanionPhraseIds.BarkLineId(ruleId, v.Text, v.Audio);

        /// <summary>
        /// All inline bark lines for the ACTIVE mod's loaded rule set, for display in the Phrase
        /// Manager. Skips <c>pool_ref</c> rules — those reuse existing phrase categories already shown
        /// in the manager, so surfacing them here would double them up.
        /// </summary>
        public IReadOnlyList<BarkLineInfo> GetAllBarkLines()
        {
            var result = new List<BarkLineInfo>();
            BarkRuleSet rules;
            rules = _engine.Rules;

            foreach (var rule in rules.AllRules)
            {
                if (rule.VariantPool == null || rule.VariantPool.Count == 0) continue; // skip pool_ref
                foreach (var v in rule.VariantPool)
                {
                    if (!v.HasText) continue;
                    string? folder = null;
                    var path = ResolveBarkAudio(v.Audio);
                    if (path != null) folder = System.IO.Path.GetDirectoryName(path);
                    result.Add(new BarkLineInfo(
                        BarkLineId(rule.Id, v), rule.Id, rule.Trigger, v.Text, v.Audio, folder));
                }
            }
            return result;
        }

        /// <summary>
        /// Public access to the per-mod voiceline path resolver, for inline voiced bubbles
        /// (e.g. the "Hey Bambi" wake ack + voice-command confirmations) that render via
        /// GigglePriority directly rather than through the bark engine.
        /// </summary>
        public static string? ResolveModAudio(string? file) => ResolveBarkAudio(file);

        // Last variant index served per voice-line rule, so PickVoiceLine avoids an immediate repeat.
        private readonly Dictionary<string, int> _lastVoiceIdx = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pick a variant (display text + resolved per-mod audio path) for the given rule id from the
        /// ACTIVE mod's loaded ruleset, avoiding an immediate repeat. Lets inline voiced bubbles (the
        /// "Hey Bambi" wake ack + voice-command confirmations) keep their text single-sourced in the
        /// bark manifests so the spoken clip always matches the on-screen line. Null when the rule or
        /// its pool is absent (caller falls back to its own text, unvoiced).
        /// </summary>
        public (string Text, string? Audio)? PickVoiceLine(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) return null;
            BarkVariant? v;
            lock (_gate)
            {
                var rule = _engine.Rules.AllRules.FirstOrDefault(
                    r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
                var pool = rule?.VariantPool;
                if (pool == null || pool.Count == 0) return null;

                var idx = _rng.Next(pool.Count);
                if (pool.Count > 1 && _lastVoiceIdx.TryGetValue(ruleId, out var last) && idx == last)
                    idx = (idx + 1) % pool.Count;
                _lastVoiceIdx[ruleId] = idx;
                v = pool[idx];
            }
            return (v.Text, ResolveBarkAudio(v.Audio));
        }

        /// <summary>
        /// Resolve a variant's voiceline filename to a full path: active mod's
        /// companion_audio folder first, embedded fallback second. Null if absent/missing.
        /// </summary>
        private static string? ResolveBarkAudio(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;

            // The ladder (packaged .ccpmod -> per-mod folder in the install dir -> per-mod folder in
            // a downloaded content pack -> shared companion_audio) lives in CompanionContentResolver
            // so bark audio, bark rules and personalities cannot drift apart.
            var pick = ModCompanionContent.ResolveActive(CompanionChannel.BarkAudio, file);
            return pick.Found ? pick.Path : null;
        }

        // ===================== speak =====================

        private void Speak(BarkPick pick)
        {
            var rule = pick.Rule;
            var avatar = App.AvatarWindow;
            if (avatar == null)
            {
                App.Logger?.Debug("[BARK] no avatar window — dropping rule={Rule}", rule.Id);
                return;
            }

            var variant = pick.Variant;
            string line = BarkEngine.ApplySubstitutions(variant.Text, pick.Context, FocusedApp());
            if (string.IsNullOrWhiteSpace(line)) return;

            // Mute egg (Fork F): an easter_egg bark while audio is fully muted shows a silent, text-only
            // bubble via Giggle (the PlayGiggleSound MasterVolume==0 guard keeps it from making sound).
            bool muted = (App.Settings?.Current?.MasterVolume ?? 0) == 0;
            if (rule.Class == BarkClass.EasterEgg && muted)
            {
                avatar.Giggle(line, mood: rule.Mood);
                App.KeywordTriggers?.MuteKeywordEcho(line, SelfEchoMuteMs);
                RaiseBarkSpoken(rule, line);
                return;
            }

            // Resolve the variant's voiceline (if any) to a real path under the active mod.
            string? audioPath = ResolveBarkAudio(variant.Audio);

            // DtRH session telemetry (local-only): a spoken bark counts toward the descent's
            // "voiceover heard". Only read the clip duration when a run is live so the global
            // bark path pays nothing off the game. No-ops elsewhere.
            if (audioPath != null && Services.Chaos.DtrhHostService.IsRunActive)
            {
                double sec = 0;
                try { using var r = new NAudio.Wave.AudioFileReader(audioPath); sec = r.TotalTime.TotalSeconds; }
                catch { /* duration is best-effort */ }
                Services.Chaos.DtrhHostService.NoteVoicelineHeard(sec);   // still counts the line if sec==0
            }

            // Route by class/priority: non-Normal or high-priority barks preempt (GigglePriority);
            // ordinary barks queue (Giggle). Safety is non-Normal, so it preempts and clears the queue.
            // When a voiceline exists it plays as the bubble's audio (no giggle sound on top).
            bool priority = pick.Preempts;
            if (priority)
                avatar.GigglePriority(line, playSound: audioPath == null, aiGenerated: false,
                    phraseAudioPath: audioPath, barkVoice: audioPath != null, mood: rule.Mood);
            else
                avatar.Giggle(line, phraseAudioPath: audioPath, barkVoice: audioPath != null, mood: rule.Mood);

            // Self-echo guard so the bubble text can't trip awareness/OCR off its own output.
            App.KeywordTriggers?.MuteKeywordEcho(line, SelfEchoMuteMs);

            // Post-fire hook: the LLM half of the character learns what the voiced half just said.
            RaiseBarkSpoken(rule, line);
        }

        /// <summary>The app {0} names in a bark line: service name, else detected name (Core substitutes "that").</summary>
        private static string? FocusedApp()
        {
            var app = App.WindowAwareness?.CurrentServiceName;
            return string.IsNullOrWhiteSpace(app) ? App.WindowAwareness?.CurrentDetectedName : app;
        }
    }
}
