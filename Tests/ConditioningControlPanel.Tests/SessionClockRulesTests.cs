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
    [InlineData(false, true)]   // after a panic the engine is off: Resume goes through Start
    [InlineData(true, false)]   // a plain pause kept the engine on: Resume is enough
    public void Resume_starts_the_engine_only_when_it_is_off(bool engineRunning, bool expected)
    {
        Assert.Equal(expected, SessionClockRules.ResumeStartsEngine(engineRunning));
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
        var fromUser = Body(startStop, "private void StartEngineFromUser()");
        Assert.Contains("SessionClockRules.StartResumesParkedSession(_isRunning, engine.IsRunning, engine.IsPaused)", fromUser);
        Assert.Contains("engine.ResumeSession();", fromUser);
        Assert.True(fromUser.IndexOf("StartEngine();", StringComparison.Ordinal)
                    < fromUser.IndexOf("engine.ResumeSession();", StringComparison.Ordinal));

        // StartEngine itself never resumes: the scheduler, the Lockdown keeper and a program or
        // remote session's own engine start all go through it.
        Assert.DoesNotContain("ResumeSession", Body(startStop, "public void StartEngine(bool systemInitiated = false)"));

        // The user's START paths: the button, its "Start normally" menu, the pause button's Resume.
        Assert.Contains("StartEngineFromUser();", Body(startStop, "private void BtnStart_Click("));
        Assert.Contains("StartEngineFromUser();", Body(startStop, "private void MenuStartNormal_Click("));
        var resume = Body(Source("MainWindow", "MainWindow.Presets.cs"), "private void BtnPauseSession_Click(");
        Assert.Contains("SessionClockRules.ResumeStartsEngine(_isRunning)) StartEngineFromUser();", resume);

        // Panic is not weakened: the stop tail still pauses the session, then stops the engine.
        var panic = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void RunPanicStopTail(bool advanceExitLadder)");
        int pause = panic.IndexOf("_sessionEngine.PauseSession();", StringComparison.Ordinal);
        int stopEngine = panic.IndexOf("StopEngine();", StringComparison.Ordinal);
        Assert.True(pause > 0 && stopEngine > pause);
        Assert.DoesNotContain("ResumeSession", panic);
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
