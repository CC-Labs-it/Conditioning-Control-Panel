using System;
using System.IO;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Two leaks against "panic stops everything", found on ccp-bugs #1464: Trigger Mode kept
/// speaking and pulsing the toy after a haptics PANIC STOP, and a remote resume undid a local
/// panic's pause. Both rules only ever refuse.
/// </summary>
public class PanicAftermathTests
{
    private const long Panic = 10_000_000; // a TickCount64 reading, ms

    // ---- B2: the post-panic quiet ---------------------------------------------------------------

    [Fact]
    public void The_quiet_lasts_five_minutes_like_emis_panic_hold_on_the_monotonic_clock()
    {
        Assert.Equal(TimeSpan.FromSeconds(300), PanicAftermath.QuietWindow);
        var until = PanicAftermath.QuietUntil(Panic);
        Assert.True(PanicAftermath.IsQuiet(Panic, until));
        Assert.True(PanicAftermath.IsQuiet(Panic + 22_000, until));    // the reporter's 'BREATHE'
        Assert.True(PanicAftermath.IsQuiet(Panic + 299_999, until));
        Assert.False(PanicAftermath.IsQuiet(Panic + 300_000, until));
        Assert.False(PanicAftermath.IsQuiet(Panic, 0));                 // never armed

        // In Core since the 7.2 port: one quiet for both heads.
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Safety", "PanicAftermath.cs"));
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Safety", "PanicAftermath.cs")),
            "a second PanicAftermath in the WPF head would split the quiet in two");
        Assert.Contains("Environment.TickCount64", src);
        Assert.DoesNotContain("UtcNow", src);
    }

    [Fact]
    public void A_panic_arms_the_quiet_and_a_user_start_ends_it()
    {
        PanicAftermath.ArmQuiet(Panic);
        Assert.True(PanicAftermath.TriggersSilenced(Panic + 22_000));
        Assert.False(PanicAftermath.TriggersSilenced(Panic + 360_000));
        PanicAftermath.EndQuiet();
        Assert.False(PanicAftermath.TriggersSilenced(Panic + 22_000));
    }

    [Fact]
    public void Every_panic_bumps_the_count_an_ai_follow_up_checks()
    {
        var before = PanicAftermath.PanicCount;
        PanicAftermath.ArmQuiet(Panic);
        Assert.True(PanicAftermath.PanicCount > before);
        PanicAftermath.EndQuiet();
        Assert.True(PanicAftermath.PanicCount > before);   // a START never rewinds it
    }

    [Fact]
    public void Trigger_mode_stays_silent_in_the_quiet_tick_queue_and_lead_in()
    {
        var speech = Source("AvatarTube", "AvatarTubeWindow.Speech.cs");
        var tick = Body(speech, "private void OnTriggerTick(object? sender, EventArgs e)");
        int guard = tick.IndexOf("PanicAftermath.TriggersSilenced()) return;", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < tick.IndexOf("ShowTriggerBubble(", StringComparison.Ordinal));

        // A trigger already queued (the legacy ladder never clears the queue): checked before the
        // haptic pulse, which fires even when muted.
        var show = Body(speech, "private void ShowTriggerBubbleImmediate(string trigger)");
        const string Drop = "PanicAftermath.TriggersSilenced()) { _isGiggling = false; ProcessNextSpeech(); return; }";
        int q = show.IndexOf(Drop, StringComparison.Ordinal);
        Assert.True(q > 0 && q < show.IndexOf("TriggerSubliminalPatternAsync", StringComparison.Ordinal));
        // ...and once more after the lead-in delay, before the trigger audio.
        // The queue moves on (ProcessNextSpeech), so speech queued behind a dropped trigger is not stranded.
        int lead = show.IndexOf(Drop, q + 1, StringComparison.Ordinal);
        Assert.True(lead > q && lead < show.IndexOf("PlayTriggerAudio(", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_panic_paths_arm_the_quiet_first()
    {
        var key = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void HandlePanicKeyPress()");
        int decide = key.IndexOf("PanicPolicy.Decide(", StringComparison.Ordinal);
        int arm = key.IndexOf("PanicAftermath.ArmQuiet();", StringComparison.Ordinal);
        // Right after the rung is decided, so an Escape that only closed the Ctrl+K palette arms
        // nothing, and before any rung can return early into a game's own panic ladder.
        Assert.True(decide > 0 && arm > decide);
        int skip = key.IndexOf("if (rung != Services.Safety.PanicPolicy.Rung.DismissSettingsPalette)", StringComparison.Ordinal);
        Assert.True(skip > decide && skip < arm && arm - skip < 120, "the palette rung must skip the quiet");
        Assert.True(arm < key.IndexOf("if (!Services.Safety.PanicPolicy.StopsSurfaces(rung)) return;", StringComparison.Ordinal));
        Assert.Equal(1, key.Split("PanicAftermath.ArmQuiet(").Length - 1);

        // A remote panic arms it too (quiet is harmless; owner, 2026-10-19).
        var remote = Body(Source("MainWindow", "MainWindow.RemoteControl.cs"), "internal bool TriggerPanicFromRemote()");
        Assert.Contains("PanicAftermath.ArmQuiet();", remote);
    }

    [Fact]
    public void A_non_user_start_never_ends_the_quiet()
    {
        // The scheduler, auto-start, remote session starts and the Lockdown keeper all go through
        // StartEngine; only the user's own START (StartEngineFromUser, whose callers are pinned in
        // SessionClockRulesTests) or the user's own Resume ends the quiet.
        var startStop = Source("MainWindow", "MainWindow.StartStop.cs");
        Assert.DoesNotContain("EndQuiet", Body(startStop, "public void StartEngine("));
        Assert.Contains("Services.Safety.PanicAftermath.EndQuiet();", Body(startStop, "internal void StartEngineFromUser()"));
        Assert.Contains("Services.Safety.PanicAftermath.EndQuiet();",
            Body(Source("MainWindow", "MainWindow.Presets.cs"), "private void BtnPauseSession_Click("));

        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var found = new System.Collections.Generic.SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)
                || rel == "Services/Safety/PanicAftermath.cs") continue;
            var text = File.ReadAllText(file);
            for (int at = text.IndexOf("PanicAftermath.EndQuiet()", StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf("PanicAftermath.EndQuiet()", at + 1, StringComparison.Ordinal))
            {
                found[rel] = found.TryGetValue(rel, out var n) ? n + 1 : 1;
            }
        }
        Assert.Equal(new System.Collections.Generic.SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["MainWindow/MainWindow.Presets.cs"] = 1,     // the user's Resume with the engine on
            ["MainWindow/MainWindow.StartStop.cs"] = 1,   // StartEngineFromUser
        }, found);
    }

    [Fact]
    public void A_panic_tells_the_scheduler_to_stand_down_like_stop_does()
    {
        var tail = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void RunPanicStopTail(bool advanceExitLadder)");
        Assert.Contains("if (App.Settings?.Current?.SchedulerEnabled == true && IsInScheduledTimeWindow())", tail);
        Assert.Contains("_manuallyStoppedDuringSchedule = true;", tail);
    }

    // ---- B1: a remote resume never undoes a local panic -----------------------------------------

    [Fact]
    public void Only_a_paused_session_leaves_a_mark()
    {
        var s = new object();
        Assert.Equal(new PanicAftermath.PanicPause(s, 3, 2), PanicAftermath.MarkPause(s, 3, true, true, 2));
        Assert.Null(PanicAftermath.MarkPause(s, 3, true, false, 2));
        Assert.Null(PanicAftermath.MarkPause(s, 3, false, true, 2));
        Assert.Null(PanicAftermath.MarkPause(null, 3, true, true, 2));
    }

    [Fact]
    public void A_local_panic_then_a_remote_resume_is_refused()
    {
        var s = new object();
        var mark = PanicAftermath.MarkPause(s, run: 1, sessionRunning: true, sessionPaused: true, pauseCount: 1);

        Assert.True(PanicAftermath.RemoteResumeRefused(mark, s, 1, sessionPaused: true, pauseCount: 1));
        // Resumed locally and paused again: a normal pause, the remote may resume it.
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, s, 1, sessionPaused: true, pauseCount: 2));
        // Resumed locally: nothing to refuse.
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, s, 1, sessionPaused: false, pauseCount: 1));
        // A different session object: the mark does not carry over.
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, new object(), 1, sessionPaused: true, pauseCount: 1));
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, null, 1, sessionPaused: true, pauseCount: 1));
    }

    [Fact]
    public void The_same_session_object_in_a_later_run_is_not_refused()
    {
        // Session.DistantDoll and friends are cached instances, and a new run resets PauseCount: the
        // SAME object, paused once in run 2, must not match a panic mark left in run 1.
        var reused = new object();
        var mark = PanicAftermath.MarkPause(reused, run: 1, sessionRunning: true, sessionPaused: true, pauseCount: 1);
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, reused, run: 2, sessionPaused: true, pauseCount: 1));

        // And the window clears the mark when a run starts or stops.
        var presets = Source("MainWindow", "MainWindow.Presets.cs");
        var started = Body(presets, "private void OnSessionStarted(object? sender, EventArgs e)");
        Assert.Contains("_sessionRun++;", started);
        Assert.Contains("_panicPause = null;", started);
        Assert.Contains("_panicPause = null;", Body(presets, "private void OnSessionStopped(object? sender, EventArgs e)"));
    }

    [Fact]
    public void A_remote_panic_then_a_remote_resume_is_allowed()
    {
        // Owner, 2026-10-19: only a LOCAL panic marks the pause. A remote panic leaves no mark, so
        // the controller may resume its own panic.
        var s = new object();
        PanicAftermath.PanicPause? noMark = null;
        Assert.False(PanicAftermath.RemoteResumeRefused(noMark, s, 1, sessionPaused: true, pauseCount: 1));

        var rc = Source("MainWindow", "MainWindow.RemoteControl.cs");
        Assert.DoesNotContain("NoteLocalPanicPause();", Body(rc, "internal bool TriggerPanicFromRemote()"));
        Assert.DoesNotContain("_panicPause =", Body(rc, "internal bool TriggerPanicFromRemote()"));
    }

    [Fact]
    public void A_local_panic_always_pauses_and_marks_the_session_engine_on_or_off()
    {
        var tail = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void RunPanicStopTail(bool advanceExitLadder)");
        int pause = tail.IndexOf("_sessionEngine.PauseSession();", StringComparison.Ordinal);
        int mark = tail.IndexOf("NoteLocalPanicPause();", StringComparison.Ordinal);
        int running = tail.IndexOf("if (_isRunning)", StringComparison.Ordinal);
        // Above the engine-on branch: a session resumed engine-off (controller connected) is paused too.
        Assert.True(pause > 0 && mark > pause && running > mark);
        Assert.True(tail.IndexOf("StopEngine();", StringComparison.Ordinal) > running);
    }

    [Fact]
    public void A_refused_remote_resume_is_reported_to_the_controller()
    {
        var rcSrc = Source("MainWindow", "MainWindow.RemoteControl.cs");
        Assert.Contains("PanicAftermath.RemoteResumeRefused(", Body(rcSrc, "internal bool LocalPanicHoldsSession"));
        var resume = Body(rcSrc, "internal bool ResumeSessionFromRemote()");
        int ask = resume.IndexOf("if (LocalPanicHoldsSession)", StringComparison.Ordinal);
        Assert.True(ask > 0 && ask < resume.IndexOf("_sessionEngine.ResumeSession();", StringComparison.Ordinal));
        Assert.Contains("return false;", resume);

        var svc = Source("Services", "RemoteControlService.cs");
        int c = svc.IndexOf("case \"resume_session\":", StringComparison.Ordinal);
        Assert.True(c > 0);
        var arm = svc.Substring(c, svc.IndexOf("break;", c, StringComparison.Ordinal) - c);
        Assert.Contains("ResumeSessionFromRemote() == false", arm);
        Assert.Contains("ReportCommandRefused(action, \"panic\"", arm);
    }

    [Fact]
    public void A_remote_start_session_cannot_undo_a_local_panic()
    {
        // start_session would stop the panic-paused run (completed:false, its XP lost) and restart
        // everything. While the local panic holds the session it is refused and reported.
        var svc = Source("Services", "RemoteControlService.cs");
        int c = svc.IndexOf("case \"start_session\":", StringComparison.Ordinal);
        Assert.True(c > 0);
        int guard = svc.IndexOf("if (MainWindowRef?.LocalPanicHoldsSession == true)", c, StringComparison.Ordinal);
        int refused = svc.IndexOf("ReportCommandRefused(action, \"panic\", \"Not allowed\");", c, StringComparison.Ordinal);
        int start = svc.IndexOf("StartSessionFromRemote(", c, StringComparison.Ordinal);
        Assert.True(guard > c && refused > guard && start > refused);

        // The individual effect verbs stay as they are (owner, 2026-10-19).
        int flash = svc.IndexOf("case \"start_flash\":", StringComparison.Ordinal);
        if (flash > 0)
            Assert.DoesNotContain("LocalPanicHoldsSession", svc.Substring(flash, svc.IndexOf("break;", flash, StringComparison.Ordinal) - flash));
    }

    // ---- unprompted companion lines and the Lockdown keeper -------------------------------------

    [Theory]
    // unprompted, engine, session, paused, optedIn, quiet => voiced
    [InlineData(false, false, false, false, false, true, true)]   // a prompted line always speaks
    [InlineData(true, true, false, false, false, false, true)]    // engine on
    [InlineData(true, true, false, false, true, true, false)]     // the quiet holds it, opted in or not
    [InlineData(true, false, true, false, false, false, true)]    // a running session
    [InlineData(true, false, true, true, false, false, false)]    // a panic-PAUSED session is not running
    [InlineData(true, false, true, true, true, false, true)]      // ...unless the player opted in
    [InlineData(true, false, false, false, false, false, false)]  // engine off, no session
    public void Idle_lines_stay_silent_after_a_panic(bool unprompted, bool engine, bool session, bool paused,
                                                     bool optedIn, bool quiet, bool voiced)
    {
        Assert.Equal(voiced, ConditioningControlPanel.Services.Companion.CompanionIdleVoice.ShouldVoice(
            unprompted, engine, session, paused, optedIn, quiet));
    }

    [Fact]
    public void Idle_voice_reads_the_quiet_and_the_session_pause()
    {
        var src = Source("Services", "Companion", "CompanionIdleVoice.cs");
        var now = src.Substring(src.IndexOf("public static bool ShouldVoiceNow(bool unprompted)", StringComparison.Ordinal)); // expression-bodied
        Assert.Contains("SessionEngine.Active?.IsPaused == true", now);
        Assert.Contains("Safety.PanicAftermath.TriggersSilenced()", now);
    }

    [Fact]
    public void A_panic_holds_engine_restarts_until_the_users_start()
    {
        PanicAftermath.ArmQuiet(Panic);
        Assert.True(PanicAftermath.HoldsEngineRestart);
        // No time limit: long after the five-minute quiet, the keeper still stands down.
        Assert.False(PanicAftermath.TriggersSilenced(Panic + 3_600_000));
        Assert.True(PanicAftermath.HoldsEngineRestart);
        PanicAftermath.ReleaseEngineHold();          // the user switched Lockdown on
        Assert.False(PanicAftermath.HoldsEngineRestart);
        PanicAftermath.ArmQuiet(Panic);
        PanicAftermath.EndQuiet();                   // the user's START
        Assert.False(PanicAftermath.HoldsEngineRestart);
        Assert.False(PanicAftermath.TriggersSilenced(Panic + 1));
    }

    [Fact]
    public void The_lockdown_keeper_never_restarts_the_engine_after_a_panic()
    {
        var keeper = Source("Services", "Haptics", "LockdownDoseKeeper.cs");
        var enforce = Body(keeper, "private void Enforce(bool atActivation)");
        int off = enforce.IndexOf("if (!App.IsEngineRunning)", StringComparison.Ordinal);
        int hold = enforce.IndexOf("PanicAftermath.HoldsEngineRestart) { _engineIdle = 0; return; }", StringComparison.Ordinal);
        int start = enforce.IndexOf("StartEngine(mw);", StringComparison.Ordinal);
        Assert.True(off > 0 && hold > off && start > hold);
        Assert.Contains("Services.Safety.PanicAftermath.ReleaseEngineHold();", keeper);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static string Source(params string[] parts)
    {
        var all = new string[parts.Length + 2];
        all[0] = RepoRoot();
        all[1] = "ConditioningControlPanel";
        Array.Copy(parts, 0, all, 2, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    /// <summary>The text of one method: from its signature to the brace that closes it.</summary>
    private static string Body(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int open = source.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(at, i - at + 1);
        }
        throw new Xunit.Sdk.XunitException("unclosed method: " + signature);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
