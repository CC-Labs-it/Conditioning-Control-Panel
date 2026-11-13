using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The player's own recorded Voice Line clips play on the idle timer again (lost in 54b4c5c653,
/// 2026-06-11, when idle chatter moved to the bark pool; Discord report: about 50 own lines heard
/// only under a preset with flash audio on). Only own clips with a file on disk, one tick in
/// IdleVoiceLines.OneIn, always as an unprompted line so the engine-off hold applies.
/// </summary>
public class IdleVoiceLinesTests
{
    private static CustomCompanionPhrase Line(string file, bool enabled = true, string category = CompanionPhraseService.VoiceLineCategory) =>
        new() { Text = "line " + file, Category = category, AudioFileName = file, Enabled = enabled };

    [Fact]
    public void Only_enabled_own_voice_lines_with_a_clip_on_disk_are_picked()
    {
        var phrases = new List<CustomCompanionPhrase>
        {
            Line("a.mp3"),
            Line("b.mp3", enabled: false),
            Line("c.mp3", category: "Custom"),
            Line("missing.mp3"),
            new() { Text = "no clip", Category = CompanionPhraseService.VoiceLineCategory, AudioFileName = null },
        };
        var folder = Path.Combine("x", "companion_audio");
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(folder, "a.mp3"), Path.Combine(folder, "b.mp3"), Path.Combine(folder, "c.mp3"),
        };

        var clips = IdleVoiceLines.OwnClips(phrases, folder, onDisk.Contains);

        Assert.Equal(new[] { Path.Combine(folder, "a.mp3") }, clips);
        Assert.Empty(IdleVoiceLines.OwnClips(null, folder, onDisk.Contains));
    }

    [Fact]
    public void One_tick_in_OneIn_goes_to_an_own_line_and_none_without_clips()
    {
        Assert.Equal(3, IdleVoiceLines.OneIn);
        Assert.True(IdleVoiceLines.TakesTurn(5, 0));
        for (int roll = 1; roll < IdleVoiceLines.OneIn; roll++) Assert.False(IdleVoiceLines.TakesTurn(5, roll));
        Assert.False(IdleVoiceLines.TakesTurn(0, 0));
    }

    [Fact]
    public void A_pick_never_repeats_the_last_clip_when_there_is_another()
    {
        var clips = new[] { "a", "b", "c" };
        var rng = new Random(7);
        string? last = null;
        for (int i = 0; i < 300; i++)
        {
            var pick = IdleVoiceLines.Pick(clips, last, rng);
            Assert.NotNull(pick);
            Assert.NotEqual(last, pick);
            last = pick;
        }
        Assert.Equal("a", IdleVoiceLines.Pick(new[] { "a" }, "a", rng));
        Assert.Null(IdleVoiceLines.Pick(Array.Empty<string>(), null, rng));
    }

    [Fact]
    public void An_own_idle_line_is_held_with_the_engine_off_and_voiced_with_it_on()
    {
        // The same rule ShowGiggle reads for the unprompted flag the idle path passes.
        Assert.False(CompanionIdleVoice.ShouldVoice(unprompted: true, engineRunning: false, speakIdleWhenEngineOff: false));
        Assert.True(CompanionIdleVoice.ShouldVoice(unprompted: true, engineRunning: true, speakIdleWhenEngineOff: false));
        Assert.False(CompanionIdleVoice.ShouldVoice(true, engineRunning: true, sessionRunning: false, sessionPaused: false,
            speakIdleWhenEngineOff: true, panicQuiet: true));
    }

    [Fact]
    public void The_idle_tick_speaks_own_lines_as_unprompted_voiced_lines()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Speech.cs"));

        var tick = Body(src, "private void OnIdleTick(");
        Assert.Contains("if (!Services.Safety.PanicAftermath.TriggersSilenced() && TrySpeakOwnVoiceLine()) return;", tick);
        Assert.True(tick.IndexOf("TrySpeakOwnVoiceLine()", StringComparison.Ordinal)
                    < tick.IndexOf("App.Bark.DispatchIdle()", StringComparison.Ordinal));

        // Through Giggle (every mute, volume and engine-off gate), never a private audio path.
        var turn = Body(src, "private bool TrySpeakOwnVoiceLine(");
        Assert.Contains("GetEnabledCustomVoiceLineFiles()", turn);
        Assert.Contains("Giggle(text, phraseAudioPath: path, barkVoice: true, unprompted: true);", turn);
        Assert.DoesNotContain("PlaySpokenAudio", turn);

        // ShowGiggle holds an unprompted line's sound and keeps "Mute Voice Lines" on voiced lines.
        Assert.Contains("bool heldIdle = !Services.Companion.CompanionIdleVoice.ShouldVoiceNow(unprompted);", src);
        Assert.Contains("if (!_isMuted && !heldIdle)", src);
        Assert.Contains("if (App.Settings?.Current?.CompanionVoiceLinesMuted == true)", src);
    }

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " not found");
        int open = src.IndexOf('{', start), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        return src.Substring(start);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "AvatarTube")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
