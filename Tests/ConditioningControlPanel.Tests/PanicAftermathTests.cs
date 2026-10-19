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
    private static readonly DateTime Panic = new(2026, 10, 19, 2, 41, 30, DateTimeKind.Utc);

    // ---- B2: the post-panic quiet ---------------------------------------------------------------

    [Fact]
    public void The_quiet_lasts_five_minutes_like_emis_panic_hold()
    {
        Assert.Equal(TimeSpan.FromSeconds(300), PanicAftermath.QuietWindow);
        var until = PanicAftermath.QuietUntil(Panic);
        Assert.True(PanicAftermath.IsQuiet(Panic, until));
        Assert.True(PanicAftermath.IsQuiet(Panic.AddSeconds(22), until));    // the reporter's 'BREATHE'
        Assert.True(PanicAftermath.IsQuiet(Panic.AddSeconds(299), until));
        Assert.False(PanicAftermath.IsQuiet(Panic.AddSeconds(300), until));
        Assert.False(PanicAftermath.IsQuiet(Panic, DateTime.MinValue));       // never armed
    }

    [Fact]
    public void A_panic_arms_the_quiet_and_a_user_start_ends_it()
    {
        PanicAftermath.ArmQuiet(Panic);
        Assert.True(PanicAftermath.TriggersSilenced(Panic.AddSeconds(22)));
        Assert.False(PanicAftermath.TriggersSilenced(Panic.AddMinutes(6)));
        PanicAftermath.EndQuiet();
        Assert.False(PanicAftermath.TriggersSilenced(Panic.AddSeconds(22)));
    }

    [Fact]
    public void Trigger_mode_skips_its_tick_in_the_quiet_before_anything_can_fire()
    {
        var speech = Source("AvatarTube", "AvatarTubeWindow.Speech.cs");
        var tick = Body(speech, "private void OnTriggerTick(object? sender, EventArgs e)");
        int guard = tick.IndexOf("PanicAftermath.TriggersSilenced(DateTime.UtcNow)) return;", StringComparison.Ordinal);
        Assert.True(guard > 0);
        // Ahead of the bubble, and so of the haptic pulse that fires even when muted.
        Assert.True(guard < tick.IndexOf("ShowTriggerBubble(", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_panic_paths_arm_the_quiet_first_and_only_a_users_start_ends_it()
    {
        var key = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void HandlePanicKeyPress()");
        int emi = key.IndexOf("App.EmiDesk?.Fire(\"panicPressed\", null)", StringComparison.Ordinal);
        int arm = key.IndexOf("PanicAftermath.ArmQuiet(DateTime.UtcNow);", StringComparison.Ordinal);
        Assert.True(emi > 0 && arm > emi);
        // Armed before any rung can return early into a game's own panic ladder.
        Assert.True(arm < key.IndexOf("PanicPolicy.Decide(", StringComparison.Ordinal));

        var remote = Body(Source("MainWindow", "MainWindow.RemoteControl.cs"), "internal void TriggerPanicFromRemote()");
        Assert.Contains("PanicAftermath.ArmQuiet(DateTime.UtcNow);", remote);

        var start = Body(Source("MainWindow", "MainWindow.StartStop.cs"), "public void StartEngine(");
        Assert.Contains("if (!systemInitiated) Services.Safety.PanicAftermath.EndQuiet();", start);
    }

    // ---- B1: a remote resume never undoes a local panic -----------------------------------------

    [Fact]
    public void Only_a_paused_session_leaves_a_mark()
    {
        var s = new object();
        Assert.Equal(new PanicAftermath.PanicPause(s, 2), PanicAftermath.MarkPause(s, true, true, 2));
        Assert.Null(PanicAftermath.MarkPause(s, true, false, 2));
        Assert.Null(PanicAftermath.MarkPause(s, false, true, 2));
        Assert.Null(PanicAftermath.MarkPause(null, true, true, 2));
    }

    [Fact]
    public void A_remote_resume_of_the_panics_own_pause_is_refused()
    {
        var s = new object();
        var mark = PanicAftermath.MarkPause(s, true, true, 1);

        Assert.True(PanicAftermath.RemoteResumeRefused(mark, s, sessionPaused: true, pauseCount: 1));
        // Resumed locally and paused again: a normal pause, the remote may resume it.
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, s, sessionPaused: true, pauseCount: 2));
        // Resumed locally: nothing to refuse.
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, s, sessionPaused: false, pauseCount: 1));
        // A different session (the old one ended): the mark does not carry over.
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, new object(), sessionPaused: true, pauseCount: 1));
        // No panic, no refusal.
        Assert.False(PanicAftermath.RemoteResumeRefused(null, s, sessionPaused: true, pauseCount: 1));
        Assert.False(PanicAftermath.RemoteResumeRefused(mark, null, sessionPaused: true, pauseCount: 1));
    }

    [Fact]
    public void Both_panic_paths_mark_the_pause_and_the_remote_resume_asks_first()
    {
        var tail = Body(Source("MainWindow", "MainWindow.xaml.cs"), "private void RunPanicStopTail(bool advanceExitLadder)");
        int pause = tail.IndexOf("_sessionEngine.PauseSession();", StringComparison.Ordinal);
        int mark = tail.IndexOf("NotePanicPause();", StringComparison.Ordinal);
        Assert.True(pause > 0 && mark > pause && mark < tail.IndexOf("StopEngine();", StringComparison.Ordinal));

        var rc = Source("MainWindow", "MainWindow.RemoteControl.cs");
        var remotePanic = Body(rc, "internal void TriggerPanicFromRemote()");
        Assert.True(remotePanic.IndexOf("NotePanicPause();", StringComparison.Ordinal)
                    > remotePanic.IndexOf("_sessionEngine.PauseSession();", StringComparison.Ordinal));

        var resume = Body(rc, "internal void ResumeSessionFromRemote()");
        int ask = resume.IndexOf("PanicAftermath.RemoteResumeRefused(", StringComparison.Ordinal);
        Assert.True(ask > 0 && ask < resume.IndexOf("_sessionEngine.ResumeSession();", StringComparison.Ordinal));
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
