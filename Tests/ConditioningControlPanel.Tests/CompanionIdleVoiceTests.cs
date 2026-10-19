using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1468 #1469 #1471 #1472: "random audio stings" with the engine off. 70 of the 132
/// engine-off sounds in the 7.1.7 trace were Idle-bark voicelines. The rule: an unprompted (idle
/// timer) line keeps its bubble but no sound while nothing runs, unless the player opted in.
/// </summary>
public class CompanionIdleVoiceTests
{
    [Theory]
    // unprompted, engine/session running, opted in => voiced
    [InlineData(true, false, false, false)]  // the bug: idle line, engine off, default setting
    [InlineData(true, false, true, true)]    // opted in: today's behaviour
    [InlineData(true, true, false, true)]    // engine or session running: unchanged
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, true)]  // a reaction to the player always speaks
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    public void Only_an_unprompted_line_with_nothing_running_and_no_opt_in_is_quiet(
        bool unprompted, bool running, bool optedIn, bool voiced)
    {
        Assert.Equal(voiced, CompanionIdleVoice.ShouldVoice(unprompted, running, optedIn));
    }

    [Theory]
    [InlineData("Idle", true)]
    [InlineData("idle", true)]
    [InlineData("IdleStateChanged", false)]   // the player went away or came back
    [InlineData("FaceFound", false)]
    [InlineData("FaceLost", false)]
    [InlineData("Blink", false)]
    [InlineData("LongStare", false)]
    [InlineData("LevelUp", false)]
    [InlineData("SessionCompleted", false)]
    [InlineData("AvatarClicked", false)]
    [InlineData("ChatMessage", false)]
    [InlineData("SettingChanged", false)]
    [InlineData("SessionSetupReady", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_idle_timer_trigger_is_unprompted(string? trigger, bool unprompted)
    {
        Assert.Equal(unprompted, CompanionIdleVoice.IsUnprompted(trigger));
    }

    [Fact]
    public void The_default_is_the_quiet_one()
    {
        Assert.False(new AppSettings().CompanionSpeaksIdleWhenEngineOff);
    }

    [Theory]
    [InlineData("builtin-ccp-default")]
    [InlineData("builtin-bambisleep")]
    [InlineData("builtin-locked")]
    [InlineData("builtin-sissyhypno")]
    public void The_logged_idle_clips_ride_the_Idle_trigger_and_the_webcam_reactions_do_not(string pack)
    {
        var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "sounds", "companion_audio",
            "mods", pack, "bark_rules.json");
        var rules = JArray.Parse(File.ReadAllText(path).TrimStart('﻿'));

        string? TriggerOf(string clipPrefix) => rules
            .OfType<JObject>()
            .Where(r => (r["variant_pool"] as JArray)?.OfType<JObject>()
                .Any(v => ((string?)v["audio"])?.StartsWith(clipPrefix, StringComparison.Ordinal) == true) == true)
            .Select(r => (string?)r["trigger"])
            .FirstOrDefault(t => t != null);

        // The clip families from the wobberjockey trace (stings-engine-off-audio.txt).
        foreach (var held in new[] { "idle_quip_", "idle_hum_", "idle_possessive_", "idle_observe_",
                                     "idle_tease_", "idle_soft_", "idle_affirm_", "mobile_idle_phone_" })
            Assert.True(CompanionIdleVoice.IsUnprompted(TriggerOf(held)), $"{pack}: {held} should be an Idle line");

        foreach (var spoken in new[] { "face_found_", "attention_lost_", "blink_t", "hold_success_",
                                       "set_hydra_on_", "setup_ready_regular_" })
        {
            var trigger = TriggerOf(spoken);
            Assert.NotNull(trigger);
            Assert.False(CompanionIdleVoice.IsUnprompted(trigger), $"{pack}: {spoken} answers the player");
        }
    }

    [Fact]
    public void The_idle_timer_raises_the_idle_trigger_and_the_tube_asks_the_rule()
    {
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var bark = File.ReadAllText(Path.Combine(root, "Services", "Companion", "BarkService.cs"));
        var speech = File.ReadAllText(Path.Combine(root, "AvatarTube", "AvatarTubeWindow.Speech.cs"));

        Assert.Equal("Idle", CompanionIdleVoice.IdleTrigger);
        Assert.True(bark.Contains("BarkContext(\"Idle\")") || bark.Contains("BarkContext(CompanionIdleVoice.IdleTrigger)"),
            "DispatchIdle must raise the Idle trigger");
        Assert.Contains("CompanionIdleVoice.IsUnprompted(rule.Trigger)", bark);
        // Speak hands the flag to BOTH routes: GigglePriority and Giggle.
        Assert.True(Count(bark, "unprompted: unprompted") >= 2, "BarkService.Speak must pass unprompted to Giggle and GigglePriority");

        Assert.Contains("CompanionIdleVoice.ShouldVoiceNow(unprompted)", speech);
        Assert.Contains("NoteHeld(\"companion-voice\", \"idle line\")", speech);
        // Every route into ShowGiggle keeps the flag: the immediate Giggle path, GigglePriority, and
        // the dequeued line (ShowSpeechBySource). Dropping any one of them breaks this count.
        Assert.True(Count(speech, "unprompted: unprompted") >= 3, "a route into ShowGiggle lost the unprompted flag");
        // The no-barks fallback on the idle tick is unprompted too.
        Assert.Contains("GetRandomBambiPhrase(), unprompted: true", speech);
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
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
