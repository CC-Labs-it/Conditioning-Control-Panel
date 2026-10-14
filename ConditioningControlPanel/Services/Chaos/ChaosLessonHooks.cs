using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Gameplay-to-lessons glue: <see cref="ChaosModeService"/> drops one-line calls in here
/// from its bubble/draft/wave seams, and this class turns them into <see cref="ChaosLessons"/>
/// ticks. All state is run-scoped (reset by <see cref="OnRunStarted"/>) except the lifetime
/// channel-hold total, which lands on <see cref="ChaosMetaState.TotalChannelSeconds"/> and
/// keeps counting after the slow_fuses lesson completes ("time holding on" in the Looking
/// Glass). Every entry point swallows its own exceptions — lesson glue must never hurt a run —
/// and the trackers that cost anything (burst positions, cursor sampling) early-out once their
/// consumer lesson is complete.
/// </summary>
public static class ChaosLessonHooks
{
    // ============================ tunables (one block, rebalance here) ============================

    /// <summary>vibe_popping: rolling window the treat pops must land inside.</summary>
    private const double VIBE_WINDOW_SEC = 5.0;
    /// <summary>chain_reaction: a pop's burst counts as "alive" for this long.</summary>
    private const double CHAIN_BURST_LIFETIME_SEC = 0.40;
    /// <summary>chain_reaction: two bursts interlace inside this distance (physical px,
    /// ~1.5x a treat burst's visual reach).</summary>
    private const double CHAIN_OVERLAP_RADIUS_PX = 170;
    /// <summary>chain_reaction: burst-history safety cap (positions tracked at once).</summary>
    private const int CHAIN_MAX_BURSTS = 64;
    /// <summary>the_pull: the cursor counts as resting under this much travel...</summary>
    private const double PULL_REST_MAX_PX = 3.0;
    /// <summary>...measured against a cursor sample between these ages (the run loop samples
    /// every 250ms; a too-fresh sample can't witness real movement).</summary>
    private const double PULL_SAMPLE_MIN_AGE_SEC = 0.15;
    private const double PULL_SAMPLE_MAX_AGE_SEC = 0.60;
    /// <summary>last_breath: the channel must have STARTED with at most this much fuse left
    /// (the same brink signal "playing with fire" judges — DefuseJudgeFuseSec).</summary>
    private const double LAST_BREATH_BRINK_SEC = 0.8;
    /// <summary>slow_fuses: stale-hold clamp — a channel can't honestly run much past the hold.</summary>
    private const double CHANNEL_MAX_SEC = ChaosTuning.DEFUSE_HOLD_MS / 1000.0 + 0.25;
    // blindfold: estimated on-screen lifetime per payload family (the heavy ones — video,
    // gif rain, dvd logos — are also checked live at defuse time).
    private const double BUSY_FLASH_SEC = 1.5;
    private const double BUSY_SUBLIMINAL_SEC = 1.2;
    private const double BUSY_OVERLAY_SEC = 3.0;
    private const double BUSY_FREEZE_SEC = 3.0;
    private const double BUSY_TEXT_SEC = 3.0;
    private const double BUSY_VIDEO_SEC = 15.0;
    private const double BUSY_CASCADE_SEC = 8.0;

    // ============================ run-scoped state ============================

    private static readonly Queue<DateTime> _vibePops = new();                    // vibe_popping window
    private static readonly List<(double X, double Y, DateTime T)> _bursts = new(); // chain_reaction
    private static (double X, double Y, DateTime T)? _cursorSample;               // the_pull (fresh)
    private static (double X, double Y, DateTime T)? _cursorSamplePrev;           // the_pull (one tick older)
    private static DateTime? _channelStartUtc;                                    // slow_fuses / time held
    private static double _channelFraction;                                       // sub-second tick remainder
    private static DateTime _busyUntilUtc = DateTime.MinValue;                    // blindfold derive
    private static int _loopDefuses;                                              // snap_field per-loop
    // silk_touch's per-loop flag lives in Core ChaosRunLessons (shared with the Avalonia head).

    /// <summary>Fresh descent: clear every per-run tracker (nothing leaks across runs).</summary>
    public static void OnRunStarted()
    {
        _vibePops.Clear();
        _bursts.Clear();
        _cursorSample = null;
        _cursorSamplePrev = null;
        _channelStartUtc = null;
        _channelFraction = 0;
        _busyUntilUtc = DateTime.MinValue;
        _loopDefuses = 0;
        ChaosRunLessons.OnRunStarted();
    }

    // ============================ pops (treat-class) ============================

    /// <summary>An ordinary treat popped (the OnBenignPopped main path — golden/heart/droplet/
    /// prism specials route elsewhere). Feeds vibe_popping, chain_reaction, the_pull,
    /// intrusive_thoughts and the blindfold screen-busy window.</summary>
    public static void OnTreatPopped(EffectBubbleSpec spec)
    {
        try
        {
            var now = DateTime.UtcNow;
            RegisterScreenBusy(spec.Payload.Kind);

            // intrusive_thoughts: the whispering treats are the subliminal variant.
            if (spec.VariantId == "subliminal") ChaosLessons.Tick("intrusive_thoughts");

            // vibe_popping: 10 treats inside a 5s rolling window (high-water).
            if (!ChaosLessons.IsComplete("vibe_popping"))
            {
                _vibePops.Enqueue(now);
                while (_vibePops.Count > 0 && (now - _vibePops.Peek()).TotalSeconds > VIBE_WINDOW_SEC)
                    _vibePops.Dequeue();
                ChaosLessons.RaiseTo("vibe_popping", _vibePops.Count);
            }
            else if (_vibePops.Count > 0) _vibePops.Clear();

            // chain_reaction: this pop's burst rises while older bursts still glow nearby —
            // one tick per interlaced pair.
            if (!ChaosLessons.IsComplete("chain_reaction"))
            {
                double x = BubbleService.ChaosLastPopXPx, y = BubbleService.ChaosLastPopYPx;
                int pairs = 0;
                for (int i = _bursts.Count - 1; i >= 0; i--)
                {
                    if ((now - _bursts[i].T).TotalSeconds > CHAIN_BURST_LIFETIME_SEC) { _bursts.RemoveAt(i); continue; }
                    double dx = _bursts[i].X - x, dy = _bursts[i].Y - y;
                    if (dx * dx + dy * dy <= CHAIN_OVERLAP_RADIUS_PX * CHAIN_OVERLAP_RADIUS_PX) pairs++;
                }
                if (pairs > 0) ChaosLessons.Tick("chain_reaction", pairs);
                if (_bursts.Count >= CHAIN_MAX_BURSTS) _bursts.RemoveAt(0);
                _bursts.Add((x, y, now));
            }
            else if (_bursts.Count > 0) _bursts.Clear();

            // the_pull: the bubble came to the hand — popped with the cursor at rest.
            if (!ChaosLessons.IsComplete("the_pull") && IsCursorAtRest()) ChaosLessons.Tick("the_pull");
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosLessonHooks.OnTreatPopped: {E}", ex.Message); }
    }

    /// <summary>A mimic prism popped (its payload-fire registers screen-busy via OnPayloadFired).</summary>
    public static void OnPrismPopped() => Safe(() => ChaosLessons.Tick("taking_chances"));

    /// <summary>A white rabbit (darter) was caught.</summary>
    public static void OnRabbitCaught() => Safe(() => ChaosLessons.Tick("rabbit_caller"));

    /// <summary>A rabbit took its FIRST smack (the Spanker equipped). Counts toward rabbit_caller:
    /// with the Spanker on, rabbits can't be caught at all, and it's the scripted first accessory —
    /// without this tick the lesson was unprovable for exactly the players being onboarded.</summary>
    public static void OnRabbitSpanked() => Safe(() => ChaosLessons.Tick("rabbit_caller"));

    /// <summary>A freeze bubble was caught (the pickup, not the Freeze Trigger toy).</summary>
    public static void OnFreezeCaught() => Safe(() => ChaosLessons.Tick("freeze_trigger"));

    // ============================ defuses + channels ============================

    /// <summary>The focus gate approved a press — a hold-to-defuse channel starts NOW.</summary>
    public static void OnChannelStarted() => Safe(() => _channelStartUtc = DateTime.UtcNow);

    /// <summary>A channel broke early ("click"/"release"; "nofocus" never started one).</summary>
    public static void OnChannelBroken() => Safe(() => EndChannel());

    /// <summary>Any defuse landed (player channel, toy, chain or zone — they all count the
    /// loop's snap_field tally and the blindfold check; the brink + channel-time lessons are
    /// channel-only).</summary>
    public static void OnDefuseCompleted(double fuseSecLeft, bool viaChannel)
    {
        try
        {
            if (viaChannel)
            {
                EndChannel();
                // last_breath: the hold STARTED on the brink (DefuseJudgeFuseSec is the
                // channel-start fuse for channel defuses — the same playing-with-fire signal).
                if (fuseSecLeft <= LAST_BREATH_BRINK_SEC) ChaosLessons.Tick("last_breath");
            }
            _loopDefuses++;
            ChaosLessons.RaiseTo("snap_field", _loopDefuses);
            if (!ChaosLessons.IsComplete("blindfold") && IsScreenBusy()) ChaosLessons.Tick("blindfold");
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosLessonHooks.OnDefuseCompleted: {E}", ex.Message); }
    }

    /// <summary>Close the open channel: bank held seconds into the lifetime total (always) and
    /// the slow_fuses lesson (whole seconds, fractional remainder carried).</summary>
    private static void EndChannel()
    {
        if (_channelStartUtc == null) return;
        double sec = (DateTime.UtcNow - _channelStartUtc.Value).TotalSeconds;
        _channelStartUtc = null;
        sec = Math.Clamp(sec, 0, CHANNEL_MAX_SEC);   // a pause-cancelled hold can leave a stale start
        ChaosMeta.State.TotalChannelSeconds += sec;  // persists with the next meta save (by design)
        if (ChaosLessons.IsComplete("slow_fuses")) return;
        _channelFraction += sec;
        long whole = (long)Math.Floor(_channelFraction);
        if (whole > 0)
        {
            _channelFraction -= whole;
            ChaosLessons.Tick("slow_fuses", whole);
        }
    }

    // ============================ detonations / loops / runs ============================

    /// <summary>A detonation (or a touched Tease) landed — this loop is no longer clean.</summary>
    public static void OnDetonation() => Safe(ChaosRunLessons.OnDetonation);

    /// <summary>A loop (wave) finished: judge silk_touch, reset the per-loop tallies.</summary>
    public static void OnLoopCompleted()
    {
        ChaosRunLessons.OnLoopCompleted();
        _loopDefuses = 0;
    }

    /// <summary>The descent ended. <paramref name="ranFullCourse"/> = the clock ran out (a quit
    /// mid-fall still pays drops, but doesn't teach end-of-run lessons or count its part-loop).</summary>
    public static void OnRunCompleted(int shieldsLeft, ChaosDifficulty difficulty, bool ranFullCourse)
    {
        try
        {
            EndChannel();   // a hold can be live at the buzzer
            if (ranFullCourse) _loopDefuses = 0;   // the final loop ends at the buzzer
            ChaosRunLessons.OnRunCompleted(shieldsLeft, difficulty, ranFullCourse);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosLessonHooks.OnRunCompleted: {E}", ex.Message); }
    }

    // ============================ drafts ============================

    /// <summary>A draft card was taken (mantras AND sins count for draft4; sins also feed surrender).</summary>
    public static void OnDraftCardTaken(bool isSin) => Safe(() =>
    {
        ChaosLessons.Tick("draft4");
        if (isSin) ChaosLessons.Tick("surrender");
    });

    // ============================ payloads / screen-busy (blindfold) ============================

    /// <summary>A video payload ran its whole slice (natural end or the 15s hard cap — not a
    /// run-closing cut).</summary>
    public static void OnVideoEndured() => Safe(() => ChaosLessons.Tick("porn_dvd"));

    /// <summary>A payload fired (detonations + prisms route through FireScaledPayload).</summary>
    public static void OnPayloadFired(EffectBubblePayloadKind kind) => Safe(() => RegisterScreenBusy(kind));

    private static void RegisterScreenBusy(EffectBubblePayloadKind kind)
    {
        if (ChaosLessons.IsComplete("blindfold")) return;
        double sec = kind switch
        {
            EffectBubblePayloadKind.Flash       => BUSY_FLASH_SEC,
            EffectBubblePayloadKind.Subliminal  => BUSY_SUBLIMINAL_SEC,
            EffectBubblePayloadKind.Overlay     => BUSY_OVERLAY_SEC,
            EffectBubblePayloadKind.BambiFreeze => BUSY_FREEZE_SEC,
            EffectBubblePayloadKind.BouncingText=> BUSY_TEXT_SEC,
            EffectBubblePayloadKind.Video       => BUSY_VIDEO_SEC,
            EffectBubblePayloadKind.GifCascade  => BUSY_CASCADE_SEC,
            EffectBubblePayloadKind.HtLink      => BUSY_CASCADE_SEC,
            _ => 0,   // Audio: not a SCREEN effect
        };
        if (sec <= 0) return;
        var until = DateTime.UtcNow.AddSeconds(sec);
        if (until > _busyUntilUtc) _busyUntilUtc = until;
    }

    /// <summary>Derived "the screen is busy" flag: the recent-payload window, or a heavy
    /// effect that is verifiably still running.</summary>
    private static bool IsScreenBusy() =>
        DateTime.UtcNow < _busyUntilUtc
        || App.Video?.IsPlaying == true
        || ChaosGifCascadeOverlay.IsRaining
        || ChaosDvdOverlay.AnyActive;

    // ============================ cursor sampling (the_pull) ============================

    /// <summary>Called from the 250ms run tick: keep two cursor samples so a pop can always
    /// find one old enough to witness real movement. No-ops once the lesson is learned.</summary>
    public static void SampleCursor()
    {
        try
        {
            if (ChaosLessons.IsComplete("the_pull")) return;
            if (!GetCursorPos(out var p)) return;
            _cursorSamplePrev = _cursorSample;
            _cursorSample = (p.X, p.Y, DateTime.UtcNow);
        }
        catch { }
    }

    private static bool IsCursorAtRest()
    {
        if (!GetCursorPos(out var cur)) return false;
        var now = DateTime.UtcNow;
        foreach (var sample in new[] { _cursorSample, _cursorSamplePrev })
        {
            if (sample == null) continue;
            double age = (now - sample.Value.T).TotalSeconds;
            if (age < PULL_SAMPLE_MIN_AGE_SEC || age > PULL_SAMPLE_MAX_AGE_SEC) continue;
            double dx = cur.X - sample.Value.X, dy = cur.Y - sample.Value.Y;
            return dx * dx + dy * dy <= PULL_REST_MAX_PX * PULL_REST_MAX_PX;
        }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

    // ============================ plumbing ============================

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { App.Logger?.Debug("ChaosLessonHooks: {E}", ex.Message); }
    }
}
