using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The happy path: a small scripted-run director for the first descents. It observes the
/// live run (elapsed progress + a few event hooks dropped in by <see cref="ChaosModeService"/>)
/// and fires the teach beats in order. Run 1 is fully scripted (naked config, lone threat,
/// one fixed draft, one darter); runs 2+ only get light, flag-guarded debuts. All state is
/// run-scoped and cleared by <see cref="OnRunEnded"/>; every beat swallows its own
/// exceptions — the script must never hurt a run.
/// </summary>
public static class ChaosHappyPath
{
    // ============================ tunables (one block) ============================

    /// <summary>Run 1 spawn-rate scale: gentler air so the teach beats land in quiet.</summary>
    private const double R1_SPAWN_RATE_MULT = DtrhRunWire.FirstRunSpawnRateMult;
    /// <summary>Run-progress (0..1) beats for the scripted run 1.</summary>
    private const double R1_THREAT_AT_PROGRESS = 0.30;
    private const double R1_DRAFT_AT_PROGRESS  = 0.55;
    /// <summary>If the threat was never defused (it blew), the draft still deals by here.</summary>
    private const double R1_DRAFT_FALLBACK_PROGRESS = 0.70;
    private const double R1_DARTER_AT_PROGRESS = 0.88;
    /// <summary>Extended trance on the scripted lone threat (the hold-to-defuse classroom).</summary>
    private const double R1_THREAT_FUSE_MULT = 3.0;
    /// <summary>The streak teach fires once the first real streak forms.</summary>
    private const int R1_STREAK_TEACH_COMBO = 3;

    /// <summary>Run 2 beats: braindrain's debut, then the guaranteed lucky bubble.</summary>
    private const double R2_BRAINDRAIN_AT_PROGRESS = 0.25;
    private const double R2_GOLDEN_AT_PROGRESS     = 0.50;

    /// <summary>The scripted run-1 draft deals 3 plain mantras from this starter pool.</summary>
    private static readonly string[] R1_DRAFT_POOL =
        { "extra_shield", "defuse_chain", "golden_touch", "welcome_shower", "heavy_drop" };

    // ============================ run-scoped state ============================

    private static ChaosRunState? _state;
    private static ChaosModeService? _svc;
    private static int _runsAtStart;      // RunsCompleted when the descent began (keys the beats)
    private static bool _scripted;        // the forced run-1 script is driving

    // run 1 beats
    private static bool _streakTaught;
    private static bool _threatSpawned;
    private static bool _threatDefuseSeen;
    private static bool _variantsJoined;
    private static bool _draftFired;
    private static bool _darterSpawned;

    // run 2 beats
    private static bool _braindrainDebuted;
    private static bool _goldenSpawned;

    // draft rigging (run 4 first sin / duo demo)
    private static int _draftsThisRun;
    private static string? _shieldRigBoonId;   // this sin's downside cannot fire this once
    private static bool _firstSinDraftPending; // SeenFirstSin flips when the rigged draft resolves

    /// <summary>Number of opening descents the happy path still shepherds (scripted run 1 + light
    /// debut/rig beats through run 4). The narrative layer stays silent while this is true.</summary>
    private const int SCRIPTED_DESCENTS = 4;

    /// <summary>True while the happy path is still teaching an early descent — used by the narrative
    /// layer to hold the Madam's voice (and so it never doubles the run-4 rigged-sin announcement).</summary>
    public static bool IsScripting => _state != null && _runsAtStart < SCRIPTED_DESCENTS;

    // ============================ entry points ============================

    /// <summary>The forced naked config for the very first descent (RunsCompleted == 0):
    /// treats only, Gentle, no drafts UI, no darters, no sins, gentler spawn air.
    /// Built raw — owned upgrades and saved settings stay out of the classroom.</summary>
    public static ChaosRunConfig BuildFirstRunConfig() => DtrhRunWire.FirstRunConfig();

    /// <summary>BeginRun calls this once the state exists. Resets every beat tracker.</summary>
    public static void OnRunStarted(ChaosRunState state, ChaosModeService svc)
    {
        _state = state;
        _svc = svc;
        _runsAtStart = ChaosMeta.State.RunsCompleted;
        _scripted = state.Config.ScriptedFirstRun;
        _streakTaught = _threatSpawned = _threatDefuseSeen = _variantsJoined = false;
        _draftFired = _darterSpawned = false;
        _braindrainDebuted = _goldenSpawned = false;
        _draftsThisRun = 0;
        _shieldRigBoonId = null;
        _firstSinDraftPending = false;
    }

    /// <summary>EndRun / teardown: drop the references; the script never outlives its run.</summary>
    public static void OnRunEnded()
    {
        _state = null;
        _svc = null;
        _scripted = false;
    }

    /// <summary>Driven from the 250ms run tick (already gated on spawning + unpaused).</summary>
    public static void Tick(double dt)
    {
        var s = _state;
        if (s == null) return;
        try
        {
            if (_scripted) TickFirstRun(s);
            else if (_runsAtStart == 1) TickSecondRun(s);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHappyPath.Tick: {E}", ex.Message); }
    }

    // ============================ run 1: the full script ============================

    private static void TickFirstRun(ChaosRunState s)
    {
        // streak teach: the first streak that forms gets one quiet line.
        if (!_streakTaught && s.Combo >= R1_STREAK_TEACH_COMBO)
        {
            _streakTaught = true;
            ChaosAnnouncerOverlay.Announce("pops in a row build a streak. it pays more.", ChaosAnnounceKind.Streak, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
            s.PushEvent("🔥 a streak. keep it alive.");
        }

        // the lone threat: one live bubble, extended trance, the hold-to-defuse classroom.
        if (!_threatSpawned && s.RunProgress >= R1_THREAT_AT_PROGRESS)
        {
            _threatSpawned = true;
            SpawnScriptedThreat(s);
        }

        // one scripted draft mid-run (drafts are otherwise disabled for this descent).
        if (!_draftFired && s.RunProgress >= R1_DRAFT_AT_PROGRESS
            && (_threatDefuseSeen || s.RunProgress >= R1_DRAFT_FALLBACK_PROGRESS))
        {
            _draftFired = FireScriptedDraft();   // a busy field (manual pause) retries next tick
        }

        // one scripted white rabbit near the end (darters are otherwise off).
        if (!_darterSpawned && s.RunProgress >= R1_DARTER_AT_PROGRESS)
        {
            _darterSpawned = true;
            try
            {
                ChaosAnnouncerOverlay.Announce("🐇 a white rabbit. catch it.", ChaosAnnounceKind.Item, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
                ChaosMeta.MarkDiscovered("bubble:darter");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildDarter(s.RunIntensity, spotlight: false));
            }
            catch (Exception ex) { App.Logger?.Debug("HappyPath darter: {E}", ex.Message); }
        }
    }

    /// <summary>The scripted lone threat: the existing hold-to-defuse tutorial beat fires
    /// HERE (BeginRun skips it for the scripted run), then a single pink live with a long,
    /// forgiving trance drifts in.</summary>
    private static void SpawnScriptedThreat(ChaosRunState s)
    {
        try
        {
            // The existing first-descent defuse teach (same copy, same flag) lands with the threat.
            if (!ChaosMeta.State.SeenDefuseTutorial)
            {
                ChaosMeta.State.SeenDefuseTutorial = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("press and HOLD a live one to snap it", ChaosAnnounceKind.Willpower, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
                s.PushEvent("✋ hold to snap. let go and it triggers.");
            }
            var pink = ChaosBubbleVariants.All.FirstOrDefault(v => v.Id == "pink");
            if (pink == null) return;
            var spec = ChaosBubbleVariants.Build(pink, s.RunIntensity,
                s.FuseTimeMult * R1_THREAT_FUSE_MULT, null, s.Config.EffectIntensity, s.BubbleScale);
            ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
            App.Bubbles?.SpawnChaosBubble(spec);
            s.PushEvent("◉ one live one. take your time with it.");
        }
        catch (Exception ex) { App.Logger?.Debug("HappyPath threat: {E}", ex.Message); }
    }

    /// <summary>A defuse completed. On run 1 the first one opens the live whitelist:
    /// pink + spiral join the run, briefly announced.</summary>
    public static void OnDefuseCompleted()
    {
        var s = _state;
        if (s == null || !_scripted) return;
        try
        {
            if (!_threatSpawned || _variantsJoined) { _threatDefuseSeen |= _threatSpawned; return; }
            _threatDefuseSeen = true;
            _variantsJoined = true;
            var enabled = s.Config.EnabledVariants;
            if (enabled != null)
            {
                if (!enabled.Contains("pink")) enabled.Add("pink");
                if (!enabled.Contains("spiral")) enabled.Add("spiral");
            }
            ChaosAnnouncerOverlay.Announce("pink and spiral drift in. hold them down too.", ChaosAnnounceKind.Item, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
            s.PushEvent("◉ more of them live now.");
        }
        catch (Exception ex) { App.Logger?.Debug("HappyPath defuse beat: {E}", ex.Message); }
    }

    /// <summary>The one scripted draft: exactly 3 plain mantras from the starter pool, no
    /// skip (the skip affordance is reveal-gated until run 3 anyway), autopick on timeout.</summary>
    private static bool FireScriptedDraft()
    {
        try
        {
            var pool = R1_DRAFT_POOL
                .Select(id => ChaosBoonPool.All.FirstOrDefault(b => b.Id == id && !b.IsCurse))
                .Where(b => b != null)
                .Cast<ChaosBoon>()
                .OrderBy(_ => Random.Shared.Next())
                .Take(3)
                .ToList();
            if (pool.Count == 0) return true;   // nothing to deal: spend the beat quietly
            return _svc?.TriggerScriptedDraft(pool) ?? true;
        }
        catch (Exception ex) { App.Logger?.Debug("HappyPath draft: {E}", ex.Message); return true; }
    }

    // ============================ run 2: light debuts ============================

    private static void TickSecondRun(ChaosRunState s)
    {
        // braindrain's debut: alone, gentler trance, announced — the standard debut pattern.
        if (!_braindrainDebuted && s.RunProgress >= R2_BRAINDRAIN_AT_PROGRESS)
        {
            _braindrainDebuted = true;
            if (!ChaosMeta.State.SeenBraindrain && VariantAllowed(s, "braindrain"))
            {
                try
                {
                    ChaosMeta.State.SeenBraindrain = true;
                    ChaosMeta.Save();
                    var v = ChaosBubbleVariants.All.FirstOrDefault(x => x.Id == "braindrain");
                    if (v != null)
                    {
                        ChaosAnnouncerOverlay.Announce("◍ BRAINDRAIN. bigger. heavier. hold it down.", ChaosAnnounceKind.Item, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
                        s.PushEvent("◍ something heavy sinks in beside you");
                        var spec = ChaosBubbleVariants.Build(v, s.RunIntensity,
                            s.FuseTimeMult * ChaosTuning.DEBUT_FUSE_MULT, null,
                            s.Config.EffectIntensity, s.BubbleScale);
                        ChaosMeta.MarkDiscovered("bubble:braindrain");
                        App.Bubbles?.SpawnChaosBubble(spec);
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("HappyPath braindrain debut: {E}", ex.Message); }
            }
        }

        // one guaranteed lucky golden bubble (gold's first-income beat lives in BankGold).
        if (!_goldenSpawned && s.RunProgress >= R2_GOLDEN_AT_PROGRESS)
        {
            _goldenSpawned = true;
            try
            {
                ChaosMeta.MarkDiscovered("bubble:golden");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildGolden());
                App.Bubbles?.PlayChime(0.30f);
            }
            catch (Exception ex) { App.Logger?.Debug("HappyPath golden: {E}", ex.Message); }
        }
    }

    private static bool VariantAllowed(ChaosRunState s, string id) =>
        s.Config.EnabledVariants == null || s.Config.EnabledVariants.Contains(id);

    // ============================ draft rigging (runs 4 / 5+) ============================

    /// <summary>
    /// Called right after every draft deal. The FIRST draft of a run can be rigged:
    /// run 4+ (RunsCompleted >= 3, once) guarantees a SHIELDED double_or_nothing — the sin's
    /// demo where the downside cannot fire; gold-driven later, the first run with both
    /// the_spanker + e_stim equipped guarantees electrified_rabbits (the duo demo).
    /// </summary>
    public static void RigDraft(List<ChaosBoon> options, ChaosRunState state)
    {
        try
        {
            _draftsThisRun++;
            if (_draftsThisRun != 1 || options.Count == 0) return;
            var meta = ChaosMeta.State;

            // run 4+: the first sin, defanged once. Accept or decline, the beat is spent.
            // (>= 3, not == 3: SeenFirstSin keeps it one-shot, and a player past run 4 —
            // sins toggled off early, or an older save — must still get the demo someday.)
            // The user's "allow sins" toggle wins — switched off, the beat waits for a later run.
            if (_runsAtStart >= 3 && !meta.SeenFirstSin && state.Config.AllowCurses)
            {
                var don = ChaosBoonPool.All.FirstOrDefault(b => b.Id == "double_or_nothing");
                if (don == null) return;
                if (!options.Any(o => o.Id == don.Id))
                {
                    int idx = options.FindIndex(o => o.IsCurse);   // swap out the sin slot if dealt
                    if (idx < 0) idx = options.Count - 1;
                    options[idx] = don;
                }
                _shieldRigBoonId = don.Id;
                _firstSinDraftPending = true;
                // Say so out loud — without this line the player can't know the demo sin is
                // defanged, and the whole teach reads as a normal gamble.
                ChaosAnnouncerOverlay.Announce("☠ a sin is on the table. the first taste is free. this once.",
                    ChaosAnnounceKind.Temptation, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
                return;
            }

            // duo demo: the gold card, dealt the first time both halves are worn.
            if (!meta.SeenDuoDemo
                && ChaosMeta.IsBoonActive("the_spanker") && ChaosMeta.IsBoonActive("e_stim"))
            {
                var duo = ChaosBoonPool.All.FirstOrDefault(b => b.Id == "electrified_rabbits");
                if (duo == null) return;
                if (!options.Any(o => o.Id == duo.Id))
                    options[options.Count - 1] = duo;
                meta.SeenDuoDemo = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("a gold card. your toys learned to work together.", ChaosAnnounceKind.Item, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
                try { App.Bark?.NotifyChaosDuoDemo(); } catch { }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHappyPath.RigDraft: {E}", ex.Message); }
    }

    /// <summary>The run-4 demo sin was picked: its downside cannot fire this once.
    /// Consumes the rig (a later natural pick of the same sin is unshielded).</summary>
    public static bool ShouldShieldSin(string boonId)
    {
        if (_shieldRigBoonId == null || boonId != _shieldRigBoonId) return false;
        _shieldRigBoonId = null;
        return true;
    }

    /// <summary>Any draft resolved (pick or skip). The run-4 first-sin beat is spent either
    /// way — declined, the sin returns unrigged in later pools.</summary>
    public static void OnDraftResolved()
    {
        if (!_firstSinDraftPending) return;
        _firstSinDraftPending = false;
        try
        {
            ChaosMeta.State.SeenFirstSin = true;
            ChaosMeta.Save();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosHappyPath.OnDraftResolved: {E}", ex.Message); }
    }
}
