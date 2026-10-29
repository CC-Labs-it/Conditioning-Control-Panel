using System;
using System.IO;
using ConditioningControlPanel.Services.Safety;
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
        // The voice intent routes here and nowhere else. On the port the intent table is Core
        // VoiceCommands; the WPF head hands the panic in through RunFor (VoiceCommandHost).
        var voice = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "AutonomyService.VoiceCommandHost.cs"));
        Assert.Contains("RunFor = VoiceRun,", voice);
        Assert.Contains("\"panic\" => () => App.MainWindowRef?.TriggerPanicFromRemote() == true,", voice);
        Assert.DoesNotContain("\"panic\" => () => App.MainWindowRef?.TriggerPanicFromRemote(),", voice);

        // It used to skip the toy, every game window, the tube, the Deeper player, a web video, a
        // running leash task (whose lines reopen a lock card) and the engine itself.
        var body = Body(RemoteControlSource(), "internal bool TriggerPanicFromRemote()");
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

    // Owner 2026-10-22 (hard rule 6): the voice panic reaches exactly as far as the panic key.
    [Theory]
    // lockdown, panic key on, leashed -> reach
    [InlineData(false, true, false, "Full")]       // normal: the whole panic
    [InlineData(false, true, true, "Full")]        // leashed, key works: the whole panic
    [InlineData(true, true, false, "Refused")]     // Lockdown holds every key
    [InlineData(true, false, false, "Refused")]    // Lockdown with its no-panic safety
    [InlineData(false, false, false, "Refused")]   // the player switched panic off
    [InlineData(true, true, true, "LeashOnly")]    // panic always works on a leash
    [InlineData(true, false, true, "LeashOnly")]
    [InlineData(false, false, true, "LeashOnly")]
    public void The_voice_panic_reaches_as_far_as_the_panic_key(
        bool lockdown, bool panicKeyEnabled, bool leashed, string expected)
        => Assert.Equal(expected, VoicePanicReach.Decide(lockdown, panicKeyEnabled, leashed).ToString());

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void The_blink_stop_and_the_voice_panic_share_one_panic_key_rule(bool lockdown, bool panicKeyEnabled)
    {
        // One rule: where the key runs, neither hands-free stop is refused for Lockdown or panic off.
        var blink = BlinkStopGate.Check(blinkTrainerRunning: false, lockdownActive: lockdown,
            panicKeyEnabled: panicKeyEnabled, strictLockEnabled: false);
        Assert.Equal(VoicePanicReach.PanicKeyRuns(lockdown, panicKeyEnabled), blink == BlinkStopGate.Block.None);
        Assert.Equal(VoicePanicReach.PanicKeyRuns(lockdown, panicKeyEnabled),
            VoicePanicReach.Decide(lockdown, panicKeyEnabled, leashed: false) == VoicePanicReach.Reach.Full);
        var blinkSrc = SourceRoots.ReadProductFile("Services", "Safety", "BlinkStopGate.cs");   // CCP.Core on the port
        Assert.Contains("VoicePanicReach.PanicKeyRuns(lockdownActive, panicKeyEnabled)", blinkSrc);
    }

    [Fact]
    public void Strict_lock_alone_leaves_the_voice_panic_whole_as_it_leaves_the_key()
    {
        // The panic key is the way out of a strict video; Strict Lock never takes it away. The voice
        // panic asks the same things the key asks and nothing about Strict Lock.
        var key = Body(MainWindowSource(), "private void OnGlobalKeyPressed(Key key)");
        Assert.DoesNotContain("StrictLockEnabled", key);
        var body = Body(RemoteControlSource(), "internal bool TriggerPanicFromRemote()");
        Assert.DoesNotContain("StrictLock", body);
        Assert.DoesNotContain("IsStrictActive", body);
        // The blink stop is stricter on purpose (owner 2026-09-28): it still refuses Strict Lock.
        Assert.Equal(BlinkStopGate.Block.StrictLock,
            BlinkStopGate.Check(false, lockdownActive: false, panicKeyEnabled: true, strictLockEnabled: true));
    }

    [Fact]
    public void The_voice_panic_asks_the_reach_rule_before_it_touches_anything()
    {
        var body = Body(RemoteControlSource(), "internal bool TriggerPanicFromRemote()");
        int gate = body.IndexOf("VoicePanicReach.Decide(", StringComparison.Ordinal);
        Assert.True(gate > 0, "the voice panic must ask VoicePanicReach");
        Assert.Contains("lockdownActive: App.Lockdown?.IsActive == true", body);
        Assert.Contains("panicKeyEnabled: App.Settings?.Current?.PanicKeyEnabled == true", body);
        Assert.Contains("leashed: Controls.Leash.LeashSurfaces.IsLeashed", body);
        // Nothing the panic does comes before the answer: not the preamble, the quiet, the safety
        // hold, the session pause, the effect stop or the engine stop.
        foreach (var step in new[]
                 {
                     "App.EmiDesk?.Fire(\"panicPressed\"", "App.Chaster?.NoteSafetyExit();", "PanicAftermath.ArmQuiet();",
                     "FirstShowService.Stop();", "LeashOnPanicPress();", "TrackPanicPressed();",
                     "_sessionEngine.PauseSession();", "App.LockCard?.Stop(", "PanicStopEverySurface();",
                     "App.Overlay?.StopSpiral();", "_manuallyStoppedDuringSchedule = true;", "if (_isRunning) StopEngine();",
                 })
        {
            int at = body.IndexOf(step, StringComparison.Ordinal);
            Assert.True(at > gate, step + " must come after the reach check");
        }

        // Refused: a log line and out. Leash only: the leash part, as the key does (LeashPanicKeyWhilePanicOff).
        var head = body[gate..body.IndexOf("App.EmiDesk?.Fire(", StringComparison.Ordinal)];
        Assert.Contains("if (reach == Services.Safety.VoicePanicReach.Reach.LeashOnly)", head);
        Assert.Contains("LeashOnPanicPress(panicRuns: false);", head);
        Assert.Contains("if (reach == Services.Safety.VoicePanicReach.Reach.Refused)", head);
        Assert.Equal(2, head.Split("return false;").Length - 1);
        Assert.Equal(1, head.Split("LeashOnPanicPress(").Length - 1);

        // One safety hold per panic (#2522 and #2526 both added one).
        Assert.Equal(1, body.Split("App.Chaster?.NoteSafetyExit()").Length - 1);
    }

    [Fact]
    public void The_panic_keys_leash_exception_is_the_one_the_voice_mirrors()
    {
        var leash = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.Leash.cs"));
        Assert.Contains("try { LeashOnPanicPress(panicRuns: false); }",
            Body(leash, "private bool LeashPanicKeyWhilePanicOff(System.Windows.Input.Key key)"));
        var key = Body(MainWindowSource(), "private void OnGlobalKeyPressed(Key key)");
        Assert.Contains("if (Controls.Leash.LeashSurfaces.IsLeashed && !LeashHoldSwallows(key)) LeashPanicKeyWhilePanicOff(key);", key);
    }

    [Fact]
    public void A_refused_voice_panic_says_nothing()
    {
        // The recogniser's "everything's off, you're safe" line only follows a panic that ran.
        // On the port the confirmation lives in Core VoiceCommands (both heads); CCP.Core.Tests
        // VoiceCommandsTests drives it end to end.
        var voice = SourceRoots.ReadProductFile("Services", "Speech", "VoiceCommands.cs").Replace("\r\n", "\n");
        var run = Body(voice, "private void ExecuteIntentAndConfirm(VoiceCommandIntent intent)");
        int refused = run.IndexOf("if (!ran) return;", StringComparison.Ordinal);
        Assert.True(refused > 0, "a refused Run must return before the confirmation");
        Assert.True(refused < run.IndexOf("Host.Say(VocabTokens.Apply(confirm)", StringComparison.Ordinal));
        Assert.True(refused < run.IndexOf("PickVoiceLine(", StringComparison.Ordinal));
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
