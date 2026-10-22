using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Night hunt 2026-10-22: what a panic did NOT reach. The stop pass and both remote-ish panics are
/// UI-thread WPF code, so the wiring is pinned by source scan. Each fact names what the player saw.
/// </summary>
public class PanicReachTests
{
    // ---- 1. the two media hosts outside VideoService ----------------------------------------------

    [Fact]
    public void The_stop_pass_ends_the_browser_video_and_pauses_the_deeper_player()
    {
        // A Takeover web video kept playing fullscreen with the engine off; a Deeper run kept playing
        // and its timeline brought the spiral, flashes and toy bands back at the next cue.
        var pass = Body(MainWindowSource(), "private void PanicStopEverySurface()");
        Assert.Contains("Step(\"web video\", PanicEndBrowserMedia);", pass);
        Assert.Contains("Step(\"Deeper player\", Views.Deeper.EnhancementPlayerWindow.PanicPauseAll);", pass);
        // Before the queue reset, so the takeover's slot is freed by its own teardown first.
        Assert.True(pass.IndexOf("PanicEndBrowserMedia", StringComparison.Ordinal)
                    < pass.IndexOf("App.InteractionQueue?.ForceReset()", StringComparison.Ordinal));

        var end = Body(MainWindowSource(), "private static void PanicEndBrowserMedia()");
        Assert.Contains("App.BrowserMedia.ForceEndCore(\"panic\")", end);   // synchronous body
        Assert.Contains("EndWebVideoTakeover()", end);
    }

    [Fact]
    public void The_deeper_panic_hook_pauses_both_modes_and_drops_fullscreen()
    {
        var player = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Deeper",
            "EnhancementPlayerWindow.xaml.cs"));
        var hook = Body(player, "internal static void PanicPauseAll()");
        Assert.Contains("w._videoSource.Pause()", hook);
        Assert.Contains("w._player.Pause()", hook);
        Assert.Contains("App.DeeperPlayer.Pause()", hook);
        Assert.Contains("ForceExitFullscreenAll()", hook);
        // Paused, never unloaded or stopped: Play picks the run up where it was.
        Assert.DoesNotContain(".Stop()", hook);
        Assert.DoesNotContain("Unload", hook);
    }

    // ---- 2. the legacy ladder (PanicOverridesAll off) ---------------------------------------------

    [Fact]
    public void The_legacy_ladder_stops_the_toy_and_the_off_engine_hosts_before_its_tail()
    {
        // With the override off, no panic rung ever called Haptics.PanicStop: the toy kept going.
        var src = MainWindowSource();
        var hosts = Body(src, "private void PanicStopOffEngineHosts()");
        Assert.Contains("App.Haptics?.PanicStop()", hosts);
        Assert.Contains("PanicEndBrowserMedia()", hosts);
        Assert.Contains("EnhancementPlayerWindow.PanicPauseAll()", hosts);

        var key = Body(src, "private void HandlePanicKeyPress()");
        Assert.Contains("PanicStopOffEngineHosts();\n                RunPanicStopTail(advanceExitLadder: false);", key);
        Assert.Contains("PanicStopOffEngineHosts();\n\n            RunPanicStopTail(advanceExitLadder: true);", key);
        // The override pass carries its own steps; the legacy helper stays out of it.
        Assert.DoesNotContain("PanicStopOffEngineHosts", Body(src, "private void PanicStopEverySurface()"));
    }

    // ---- 3. the voice panic ("red", "safe word", "stop everything") --------------------------------

    [Fact]
    public void The_voice_panic_runs_the_panic_keys_stop_pass_and_stops_the_engine()
    {
        // The voice intent routes here and nowhere else.
        var voice = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "AutonomyService.VoiceCommands.cs"));
        Assert.Contains("Execute = () => App.MainWindowRef?.TriggerPanicFromRemote(),", voice);

        // It used to skip the toy, every game window, the tube, the Deeper player, a web video, a
        // running leash task (whose lines reopen a lock card) and the engine itself.
        var body = Body(RemoteControlSource(), "internal void TriggerPanicFromRemote()");
        foreach (var call in new[]
                 {
                     "PanicStopEverySurface();",
                     "LeashOnPanicPress();",
                     "App.Chaster?.NoteSafetyExit();",
                     "FirstShowService.Stop();",
                     "App.LockCard?.Stop(dismissOpenCards: true);",
                     "_manuallyStoppedDuringSchedule = true;",
                     "if (_isRunning) StopEngine();",
                     "PanicAftermath.ArmQuiet();",
                 })
            Assert.Contains(call, body);
        // The session is paused before the engine stops, as the panic key's tail does.
        Assert.True(body.IndexOf("_sessionEngine.PauseSession();", StringComparison.Ordinal)
                    < body.IndexOf("StopEngine();", StringComparison.Ordinal));
        // Still never the press that quits the app.
        Assert.DoesNotContain("Shutdown()", body);
        Assert.DoesNotContain("_panicPressCount", body);
    }

    [Theory]
    [InlineData(false, true, true)]    // the key could stop it: so may the voice
    [InlineData(true, true, false)]    // Lockdown holds every key
    [InlineData(false, false, false)]  // the player switched panic off
    [InlineData(true, false, false)]
    public void The_voice_panic_stops_the_engine_only_where_the_panic_key_could(
        bool lockdown, bool panicKeyEnabled, bool expected)
    {
        // Hard rule 6: a word must never end a Lockdown the panic key cannot touch.
        Assert.Equal(expected, ConditioningControlPanel.Services.Safety.VoicePanicReach.StopsEngine(lockdown, panicKeyEnabled));
    }

    [Fact]
    public void The_voice_panic_asks_the_reach_rule_before_it_stops_the_engine()
    {
        var body = Body(RemoteControlSource(), "internal void TriggerPanicFromRemote()");
        int gate = body.IndexOf("VoicePanicReach.StopsEngine(", StringComparison.Ordinal);
        Assert.True(gate > 0, "the voice panic must ask VoicePanicReach");
        Assert.Contains("lockdownActive: App.Lockdown?.IsActive == true", body);
        Assert.Contains("panicKeyEnabled: App.Settings?.Current?.PanicKeyEnabled == true", body);
        Assert.True(gate < body.IndexOf("_manuallyStoppedDuringSchedule = true;", StringComparison.Ordinal));
        Assert.True(gate < body.IndexOf("if (_isRunning) StopEngine();", StringComparison.Ordinal));
    }

    // ---- 4. what came back on its own after a panic ------------------------------------------------

    [Fact]
    public void Keyword_triggers_wait_out_the_panic_quiet_before_they_dispatch()
    {
        // A keyword on screen or typed flashed, played and pulsed the toy seconds after a panic.
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "KeywordTriggerService.cs"));
        var merged = Body(src, "private async Task DispatchMergedAsync(");
        int gate = merged.IndexOf("Safety.PanicAftermath.TriggersSilenced()", StringComparison.Ordinal);
        Assert.True(gate > 0, "the merged dispatch must ask the panic quiet");
        Assert.True(gate < merged.IndexOf("DispatchResponseAsync(", StringComparison.Ordinal));
        Assert.True(gate < merged.IndexOf("DispatchActionsAsync(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_controllers_panic_arms_the_quiet_but_leaves_no_pause_mark()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "RemoteControlService.cs"));
        var all = Body(src, "private void StopAllRemoteEffects(RemoteEndCause cause)");
        Assert.Contains("if (force) Safety.PanicAftermath.ArmQuiet();", all);
        Assert.DoesNotContain("NoteLocalPanicPause", all);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static string MainWindowSource() => File.ReadAllText(Path.Combine(
        RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));

    private static string RemoteControlSource() => File.ReadAllText(Path.Combine(
        RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.RemoteControl.cs"));

    /// <summary>One member's text: from its signature to the next member at class indent.</summary>
    private static string Body(string src, string signature)
    {
        src = src.Replace("\r\n", "\n");   // the checkout may be CRLF or LF
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing: " + signature);
        int end = src.Length;
        foreach (var marker in new[] { "\n        /// <summary>", "\n        private ", "\n        internal ", "\n        public " })
        {
            var at = src.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
            if (at >= 0 && at < end) end = at;
        }
        return src[start..end];
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
