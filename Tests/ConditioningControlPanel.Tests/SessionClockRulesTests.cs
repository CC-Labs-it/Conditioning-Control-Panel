using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1464, "the timer got stuck at 1:14". A panic paused the session and stopped the
/// engine; Start then ran a bare engine over the parked session and its clock never moved again.
/// The rule: a session clock on screen either counts or says it is paused. Panic still pauses the
/// session and stops the engine.
/// </summary>
public class SessionClockRulesTests
{
    // ---- the clock face -------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, SessionClockRules.ClockFace.None)]
    [InlineData(false, true, SessionClockRules.ClockFace.None)]      // a stale pause flag never shows a clock
    [InlineData(true, false, SessionClockRules.ClockFace.Counting)]
    [InlineData(true, true, SessionClockRules.ClockFace.Paused)]
    public void The_face_follows_the_session(bool running, bool paused, SessionClockRules.ClockFace expected)
    {
        Assert.Equal(expected, SessionClockRules.Face(running, paused));
    }

    [Fact]
    public void Only_a_paused_clock_carries_the_mark()
    {
        Assert.Equal(" [PAUSED]", SessionClockRules.PauseMark(true, "PAUSED"));
        Assert.Equal("", SessionClockRules.PauseMark(false, "PAUSED"));
        Assert.Equal(" []", SessionClockRules.PauseMark(true, null!));
    }

    [Fact]
    public void The_stop_button_says_paused_instead_of_looking_stuck()
    {
        var t = new TimeSpan(0, 1, 14);
        Assert.Equal("STOP SESSION (01:14) [PAUSED]",
            SessionClockLabel.StopButton(t, true, "STOP SESSION ({0}:{1})", "STOP SESSION", " [PAUSED]"));
        Assert.Equal("STOP SESSION [PAUSED]",
            SessionClockLabel.StopButton(t, false, "STOP SESSION ({0}:{1})", "STOP SESSION", " [PAUSED]"));
        // The old four-argument call is unchanged.
        Assert.Equal("STOP SESSION (01:14)",
            SessionClockLabel.StopButton(t, true, "STOP SESSION ({0}:{1})", "STOP SESSION"));
    }

    // ---- Start and Resume -----------------------------------------------------------------

    [Theory]
    [InlineData(true, true, true, true)]      // the reporter: panic parked it, the user pressed Start
    [InlineData(false, true, true, false)]    // the engine did not start: nothing to resume into
    [InlineData(true, true, false, false)]    // already counting
    [InlineData(true, false, true, false)]    // no session (a stale pause flag)
    [InlineData(true, false, false, false)]
    public void Start_resumes_a_parked_session_only(bool engineRunning, bool running, bool paused, bool expected)
    {
        Assert.Equal(expected, SessionClockRules.StartResumesParkedSession(engineRunning, running, paused));
    }

    [Theory]
    [InlineData(false, false, true)]    // after a panic the engine is off: Resume goes through Start
    [InlineData(true, false, false)]    // a plain pause kept the engine on: Resume is enough
    [InlineData(false, true, false)]    // a remote controller owns the engine: START is refused, so is this
    [InlineData(true, true, false)]
    public void Resume_starts_the_engine_only_when_it_is_off_and_local(bool engineRunning, bool controller, bool expected)
    {
        Assert.Equal(expected, SessionClockRules.ResumeStartsEngine(engineRunning, controller));
    }

    // ---- a failing step is reported once per session ------------------------------------------

    [Fact]
    public void Only_the_first_failure_of_each_step_is_reported_and_repeats_are_counted()
    {
        var log = new SessionClockRules.StepFailures();
        Assert.False(log.Any);
        Assert.Equal("", log.Summary());

        Assert.True(log.Note("tick:ramp"));
        for (int i = 0; i < 119; i++) Assert.False(log.Note("tick:ramp"));
        Assert.True(log.Note("tick:bubbles"));
        Assert.False(log.Note("tick:bubbles"));

        Assert.True(log.Any);
        Assert.Equal(120, log.Count("tick:ramp"));
        Assert.Equal(0, log.Count("stop"));
        Assert.Equal("tick:bubbles x2, tick:ramp x120", log.Summary());

        log.Clear();   // a new session starts with a clean slate
        Assert.False(log.Any);
        Assert.True(log.Note("tick:ramp"));
    }

    [Fact]
    public void Step_failures_reach_the_crash_log_once_and_the_stop_writes_one_summary()
    {
        var src = Source("Services", "Session", "SessionEngine.cs");
        var report = Body(src, "private void ReportStepFailure(string step, Exception ex)");
        Assert.Contains("if (!_stepFailures.Note(step)) return;", report);
        Assert.Contains("App.LogCrashDetails(", report);
        Assert.DoesNotContain("Session tick step failed", src);   // the old per-tick warning

        Assert.Contains("_stepFailures.Clear();", Body(src, "public async Task StartSessionAsync("));
        var stop = Body(src, "public void StopSession(");
        Assert.Contains("_stepFailures.Summary()", stop);
        Assert.Contains("internal static void LogCrashDetails(string source, Exception? ex)",
            Source("App.xaml.cs"));
    }

    // ---- a throwing step never stops the clock ---------------------------------------------

    [Fact]
    public void A_throwing_step_is_reported_and_the_rest_runs()
    {
        var ran = new List<string>();
        var errors = new List<Exception>();
        void OnError(Exception ex) { lock (errors) errors.Add(ex); }

        Assert.True(SessionClockRules.RunStep(() => ran.Add("progress"), OnError));
        Assert.False(SessionClockRules.RunStep(() => throw new InvalidOperationException("feature start"), OnError));
        Assert.True(SessionClockRules.RunStep(() => ran.Add("bubbles"), OnError));

        Assert.Equal(new[] { "progress", "bubbles" }, ran);
        Assert.Single(errors);
    }

    [Fact]
    public void A_throwing_reporter_is_swallowed_too()
    {
        Assert.False(SessionClockRules.RunStep(() => throw new Exception("step"), _ => throw new Exception("log")));
        Assert.False(SessionClockRules.RunStep(() => throw new Exception("step"), null));
    }

    // ---- the wiring -----------------------------------------------------------------------

    [Fact]
    public void Every_step_of_the_session_tick_is_guarded()
    {
        var tick = Body(Source("Services", "Session", "SessionEngine.cs"), "private void MainTimer_Tick(");
        foreach (var step in new[] { "ProgressUpdated?.Invoke", "CheckPhaseTransition(", "UpdateRampingValues(",
                                     "CheckDelayedFeatures(", "HandleIntermittentBubbles(" })
        {
            int at = tick.IndexOf(step, StringComparison.Ordinal);
            Assert.True(at > 0, step);
            int guard = tick.LastIndexOf("SessionClockRules.RunStep(", at, StringComparison.Ordinal);
            Assert.True(guard > 0 && tick.IndexOf(';', guard) > at, step + " runs unguarded");
        }
    }

    [Fact]
    public void A_throwing_teardown_step_cannot_skip_SessionStopped()
    {
        var stop = Body(Source("Services", "Session", "SessionEngine.cs"), "public void StopSession(");
        int fired = stop.IndexOf("SessionStopped?.Invoke", StringComparison.Ordinal);
        Assert.True(fired > 0);
        var before = stop.Substring(0, fired);
        Assert.Contains("Step(CloseCornerGif);", before);
        Assert.Contains("Step(RestoreSettings);", before);
        Assert.DoesNotContain("\n            RestoreSettings();", before);
    }

    [Fact]
    public void Only_the_users_start_resumes_and_panic_still_pauses_and_stops()
    {
        var startStop = Source("MainWindow", "MainWindow.StartStop.cs");
        var fromUser = Body(startStop, "internal void StartEngineFromUser()");
        Assert.Contains("SessionClockRules.StartResumesParkedSession(_isRunning, engine.IsRunning, engine.IsPaused)", fromUser);
        int start = fromUser.IndexOf("StartEngine(resumingSession: resuming);", StringComparison.Ordinal);
        int resumed = fromUser.IndexOf("engine.ResumeSession();", StringComparison.Ordinal);
        int reasserted = fromUser.IndexOf("engine.ReassertScheduleAfterEngineStart();", StringComparison.Ordinal);
        Assert.True(start > 0 && resumed > start && reasserted > resumed);
        // "resuming" is read BEFORE the start, while the session is still paused.
        Assert.True(fromUser.IndexOf("var resuming = _sessionEngine?.IsRunning == true && _sessionEngine.IsPaused;",
            StringComparison.Ordinal) is var at && at > 0 && at < start);

        // StartEngine itself never resumes: the scheduler, the Lockdown keeper and a program or
        // remote session's own engine start all go through it. A resume is not one more session.
        var engineStart = Body(startStop, "public void StartEngine(bool systemInitiated = false, bool resumingSession = false)");
        Assert.DoesNotContain("ResumeSession", engineStart);
        Assert.Contains("if (!systemInitiated && !resumingSession) settings.TotalSessions++;", engineStart);
        Assert.Contains("if (!systemInitiated) App.Achievements?.CheckRelapse();", engineStart);

        // The user's START paths: the button, its "Start normally" menu, the pause button's Resume.
        Assert.Contains("StartEngineFromUser();", Body(startStop, "private void BtnStart_Click("));
        Assert.Contains("StartEngineFromUser();", Body(startStop, "private void MenuStartNormal_Click("));
        var resume = Body(Source("MainWindow", "MainWindow.Presets.cs"), "private void BtnPauseSession_Click(");
        Assert.Contains("SessionClockRules.ResumeStartsEngine(_isRunning, App.RemoteControl?.ControllerConnected == true))", resume);

        // Panic is not weakened: the stop tail still pauses the session, then stops the engine.
        var panic = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void RunPanicStopTail(bool advanceExitLadder)");
        int pause = panic.IndexOf("_sessionEngine.PauseSession();", StringComparison.Ordinal);
        int stopEngine = panic.IndexOf("StopEngine();", StringComparison.Ordinal);
        Assert.True(pause > 0 && stopEngine > pause);
        Assert.DoesNotContain("ResumeSession", panic);
    }

    [Fact]
    public void StartEngineFromUser_has_exactly_the_user_callers()
    {
        // A system path (scheduler, Lockdown keeper, program or remote start, auto-start) joining
        // this list would resume a session a panic paused without the user asking. Pinned.
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["AvatarTube/AvatarTubeWindow.ChatInput.cs"] = 1,   // the companion menu's Start click
            ["MainWindow/MainWindow.Presets.cs"] = 1,           // Resume with the engine off
            ["MainWindow/MainWindow.StartStop.cs"] = 2,         // START, "Start normally"
        };
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)) continue;
            int n = Count(File.ReadAllText(file), "StartEngineFromUser();");
            if (n > 0) found[rel] = n;
        }
        Assert.Equal(expected, found);

        var startStop = Source("MainWindow", "MainWindow.StartStop.cs");
        Assert.Contains("StartEngineFromUser();", Body(startStop, "private void BtnStart_Click("));
        Assert.Contains("StartEngineFromUser();", Body(startStop, "private void MenuStartNormal_Click("));
    }

    [Fact]
    public void A_user_resume_puts_the_sessions_own_schedule_back()
    {
        var body = Body(Source("Services", "Session", "SessionEngine.cs"), "public void ReassertScheduleAfterEngineStart()");
        Assert.Contains("_pendingFeatureStarts", body);                      // not-yet-due features stay off (#483)
        Assert.Contains("App.LockCard?.Start(SessionWindowMinutes)", body);  // the session's window (#736)
        Assert.Contains("App.MindWipe.StartSession(", body);                 // the session's Mind Wipe (#1304)
        foreach (var name in new[] { "\"flash\"", "\"subliminal\"", "\"bouncing text\"", "\"mandatory videos\"",
                                     "\"lock cards\"", "\"bubble count\"" })
            Assert.Contains(name, body);
    }

    [Fact]
    public void Every_clock_is_repainted_on_engine_start_stop_and_on_pause_and_resume()
    {
        var startStop = Source("MainWindow", "MainWindow.StartStop.cs");
        var update = Body(startStop, "private void UpdateStartButton()");
        Assert.Equal(2, Count(update, "RefreshSessionClocks();"));

        var presets = Source("MainWindow", "MainWindow.Presets.cs");
        var watch = Body(presets, "private void WatchSessionClocks(SessionEngine engine)");
        Assert.Contains("engine.SessionPaused +=", watch);
        Assert.Contains("engine.SessionResumed +=", watch);

        // All three places that build the engine watch it.
        Assert.Contains("WatchSessionClocks(_sessionEngine);", presets);
        Assert.Contains("WatchSessionClocks(_sessionEngine);", Source("MainWindow", "MainWindow.ProgramsTab.cs"));
        Assert.Contains("WatchSessionClocks(_sessionEngine);", Source("MainWindow", "MainWindow.RemoteControl.cs"));
        Assert.Equal(3, Count(presets, "new SessionEngine(") + Count(Source("MainWindow", "MainWindow.ProgramsTab.cs"), "new Services.SessionEngine(")
                        + Count(Source("MainWindow", "MainWindow.RemoteControl.cs"), "new Services.SessionEngine("));

        var repaint = Body(presets, "private void RefreshSessionClocks()");
        Assert.Contains("SessionClockRules.PauseMark(", repaint);
        Assert.DoesNotContain("Save(", repaint);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static int Count(string text, string needle)
    {
        int n = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal)) n++;
        return n;
    }

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
