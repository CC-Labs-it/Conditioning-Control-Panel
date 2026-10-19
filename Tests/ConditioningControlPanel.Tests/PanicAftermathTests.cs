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

        var src = Source("Services", "Safety", "PanicAftermath.cs");
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
    public void Trigger_mode_stays_silent_in_the_quiet_tick_queue_and_lead_in()
    {
        var speech = Source("AvatarTube", "AvatarTubeWindow.Speech.cs");
        var tick = Body(speech, "private void OnTriggerTick(object? sender, EventArgs e)");
        int guard = tick.IndexOf("PanicAftermath.TriggersSilenced()) return;", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < tick.IndexOf("ShowTriggerBubble(", StringComparison.Ordinal));

        // A trigger already queued (the legacy ladder never clears the queue): checked before the
        // haptic pulse, which fires even when muted.
        var show = Body(speech, "private void ShowTriggerBubbleImmediate(string trigger)");
        int q = show.IndexOf("PanicAftermath.TriggersSilenced()) { _isGiggling = false; return; }", StringComparison.Ordinal);
        Assert.True(q > 0 && q < show.IndexOf("TriggerSubliminalPatternAsync", StringComparison.Ordinal));
        // ...and once more after the lead-in delay, before the trigger audio.
        int lead = show.IndexOf("PanicAftermath.TriggersSilenced()) { _isGiggling = false; return; }", q + 1, StringComparison.Ordinal);
        Assert.True(lead > q && lead < show.IndexOf("PlayTriggerAudio(", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_panic_paths_arm_the_quiet_first()
    {
        var key = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void HandlePanicKeyPress()");
        int emi = key.IndexOf("App.EmiDesk?.Fire(\"panicPressed\", null)", StringComparison.Ordinal);
        int arm = key.IndexOf("PanicAftermath.ArmQuiet();", StringComparison.Ordinal);
        Assert.True(emi > 0 && arm > emi);
        // Armed before any rung can return early into a game's own panic ladder.
        Assert.True(arm < key.IndexOf("PanicPolicy.Decide(", StringComparison.Ordinal));

        // A remote panic arms it too (quiet is harmless; owner, 2026-10-19).
        var remote = Body(Source("MainWindow", "MainWindow.RemoteControl.cs"), "internal void TriggerPanicFromRemote()");
        Assert.Contains("PanicAftermath.ArmQuiet();", remote);
    }

    [Fact]
    public void A_non_user_start_never_ends_the_quiet()
    {
        // The scheduler, auto-start, remote and program session starts and the Lockdown keeper all
        // go through StartEngine; only the user's own START ends the quiet.
        var startStop = Source("MainWindow", "MainWindow.StartStop.cs");
        Assert.DoesNotContain("EndQuiet", Body(startStop, "public void StartEngine("));
        Assert.Contains("Services.Safety.PanicAftermath.EndQuiet();", Body(startStop, "internal void StartEngineFromUser()"));

        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        int callers = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)
                || rel == "Services/Safety/PanicAftermath.cs") continue;
            var text = File.ReadAllText(file);
            for (int at = text.IndexOf("PanicAftermath.EndQuiet()", StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf("PanicAftermath.EndQuiet()", at + 1, StringComparison.Ordinal))
            {
                Assert.Equal("MainWindow/MainWindow.StartStop.cs", rel);
                callers++;
            }
        }
        Assert.Equal(1, callers);
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
        Assert.DoesNotContain("NoteLocalPanicPause();", Body(rc, "internal void TriggerPanicFromRemote()"));
        Assert.DoesNotContain("_panicPause =", Body(rc, "internal void TriggerPanicFromRemote()"));
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
        var resume = Body(Source("MainWindow", "MainWindow.RemoteControl.cs"), "internal bool ResumeSessionFromRemote()");
        int ask = resume.IndexOf("PanicAftermath.RemoteResumeRefused(", StringComparison.Ordinal);
        Assert.True(ask > 0 && ask < resume.IndexOf("_sessionEngine.ResumeSession();", StringComparison.Ordinal));
        Assert.Contains("return false;", resume);

        var svc = Source("Services", "RemoteControlService.cs");
        int c = svc.IndexOf("case \"resume_session\":", StringComparison.Ordinal);
        Assert.True(c > 0);
        var arm = svc.Substring(c, svc.IndexOf("break;", c, StringComparison.Ordinal) - c);
        Assert.Contains("ResumeSessionFromRemote() == false", arm);
        Assert.Contains("ReportCommandRefused(action, \"panic\"", arm);
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
