using System;
using Serilog;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The loop and run-end judgments of WPF <c>ChaosLessonHooks</c> (silk_touch, popup_notification,
/// extreme_tier), shared by both heads. WPF ChaosLessonHooks delegates here and keeps its
/// head-only trackers (cursor, channel hold, screen-busy); Avalonia ChaosRunHost calls it
/// directly. Every entry point swallows its own exceptions: lesson glue must never hurt a run.
/// </summary>
public static class ChaosRunLessons
{
    private static bool _loopDirty;   // silk_touch: a detonation landed this loop
    private static int _loopDefuses;  // snap_field: snaps this loop

    /// <summary>last_breath: the channel must have STARTED with at most this much fuse left
    /// (the same brink signal "playing with fire" judges - DefuseJudgeFuseSec).</summary>
    public const double LAST_BREATH_BRINK_SEC = 0.8;

    /// <summary>Fresh descent: nothing leaks across runs.</summary>
    public static void OnRunStarted() { _loopDirty = false; _loopDefuses = 0; }

    /// <summary>A defuse completed (WPF ChaosLessonHooks.OnDefuseCompleted): a channel started on
    /// the brink teaches last_breath; every snap raises this loop's snap_field count.</summary>
    public static void OnDefuseCompleted(double fuseSecLeft, bool viaChannel)
    {
        try
        {
            if (viaChannel && fuseSecLeft <= LAST_BREATH_BRINK_SEC) ChaosLessons.Tick("last_breath");
            _loopDefuses++;
            ChaosLessons.RaiseTo("snap_field", _loopDefuses);
        }
        catch (Exception ex) { Log.Debug("ChaosRunLessons.OnDefuseCompleted: {E}", ex.Message); }
    }

    /// <summary>A detonation (or a touched Tease) landed: this loop is no longer clean.</summary>
    public static void OnDetonation() => _loopDirty = true;

    /// <summary>A loop (wave) finished: judge silk_touch, reset the per-loop flag.</summary>
    public static void OnLoopCompleted()
    {
        try
        {
            _loopDefuses = 0;
            if (!_loopDirty) ChaosLessons.Tick("silk_touch");
            _loopDirty = false;
        }
        catch (Exception ex) { Log.Debug("ChaosRunLessons.OnLoopCompleted: {E}", ex.Message); }
    }

    /// <summary>The descent ended. <paramref name="ranFullCourse"/> = the clock ran out (a quit
    /// mid-fall doesn't teach end-of-run lessons or count its part-loop).</summary>
    public static void OnRunCompleted(int shieldsLeft, ChaosDifficulty difficulty, bool ranFullCourse)
    {
        try
        {
            if (!ranFullCourse) return;
            OnLoopCompleted();   // the final loop ends at the buzzer, not a wave transition
            if (shieldsLeft > 0) ChaosLessons.Tick("popup_notification");
            // extreme_tier asks for Relentless (Hard); an Inescapable (Extreme) clear counts too.
            if (difficulty >= ChaosDifficulty.Hard) ChaosLessons.Tick("extreme_tier");
        }
        catch (Exception ex) { Log.Debug("ChaosRunLessons.OnRunCompleted: {E}", ex.Message); }
    }
}
