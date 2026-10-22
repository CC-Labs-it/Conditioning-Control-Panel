using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The companion's Audio effect (owner, 2026-10-13): the player's own files in the assets
/// audio folder first; with none, a RECORDED clip from the active mod. CCP Default and Locked never
/// borrow the Bambi-voiced clips (<c>ModAudioPolicy</c>), and nothing is ever synthesised.</summary>
public class AudioActionPoolTests
{
    private static readonly string[] Own = { "own.mp3" };
    private static readonly string[] Voice = { "voice.mp3" };
    private static readonly string[] Shared = { "GOOD GIRL.mp3" };
    private static readonly string[] Neutral = { "let-go.mp3" };
    private static readonly string[] Empty = Array.Empty<string>();

    private static (MediaCommand.AudioPool Pool, IReadOnlyList<string> Files) Choose(
        string[] own, string? modId, string[] voice, string[] shared, string[] neutral)
        => MediaCommand.ChooseAudioPool(own, modId, () => voice, () => shared, () => neutral);

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.LockedId)]
    [InlineData(null)]
    public void The_players_own_files_always_win(string? modId)
    {
        var (pool, files) = Choose(Own, modId, Voice, Shared, Neutral);

        Assert.Equal(MediaCommand.AudioPool.Own, pool);
        Assert.Same(Own, files);
    }

    [Fact]
    public void Own_files_win_without_reading_any_mod_pool()
    {
        var read = 0;
        IReadOnlyList<string>? Count() { read++; return Voice; }

        MediaCommand.ChooseAudioPool(Own, BuiltInMods.BambiSleepId, Count, Count, Count);

        Assert.Equal(0, read);
    }

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.SissyHypnoId)]
    [InlineData(BuiltInMods.LockedId)]
    [InlineData(BuiltInMods.DronificationId)]
    public void An_empty_folder_falls_back_to_the_mods_own_voice(string modId)
    {
        var (pool, files) = Choose(Empty, modId, Voice, Shared, Neutral);

        Assert.Equal(MediaCommand.AudioPool.ModVoice, pool);
        Assert.Same(Voice, files);
    }

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.SissyHypnoId)]
    public void A_mod_that_may_borrow_them_gets_the_shared_clips_next(string modId)
    {
        var (pool, files) = Choose(Empty, modId, Empty, Shared, Neutral);

        Assert.Equal(MediaCommand.AudioPool.SharedClips, pool);
        Assert.Same(Shared, files);
    }

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.LockedId)]
    [InlineData(null)]
    [InlineData("  ")]
    public void Default_and_Locked_get_neutral_words_and_never_read_the_Bambi_clips(string? modId)
    {
        var sharedRead = false;
        var (pool, files) = MediaCommand.ChooseAudioPool(
            Empty, modId, () => Empty, () => { sharedRead = true; return Shared; }, () => Neutral);

        Assert.Equal(MediaCommand.AudioPool.NeutralClips, pool);
        Assert.Same(Neutral, files);
        Assert.False(sharedRead);
    }

    [Fact]
    public void An_unknown_mod_never_speaks_with_the_voice_line_folder()
    {
        // With no mod id that folder resolves to the Bambi baseline.
        var voiceRead = false;
        var (pool, _) = MediaCommand.ChooseAudioPool(
            Empty, null, () => { voiceRead = true; return Voice; }, () => Shared, () => Neutral);

        Assert.Equal(MediaCommand.AudioPool.NeutralClips, pool);
        Assert.False(voiceRead);
    }

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(null)]
    public void Nothing_anywhere_is_nothing_to_play(string? modId)
    {
        var (pool, files) = MediaCommand.ChooseAudioPool(null, modId, () => null, () => null, () => null);

        Assert.Equal(MediaCommand.AudioPool.None, pool);
        Assert.Empty(files);
    }

    [Fact]
    public void A_mod_with_shared_clips_does_not_fall_through_to_neutral_words()
    {
        // Same order SpokenClipRule gives a spoken phrase.
        var (pool, _) = Choose(Empty, BuiltInMods.BambiSleepId, Empty, Empty, Neutral);

        Assert.Equal(MediaCommand.AudioPool.None, pool);
    }

    [Fact]
    public void The_failure_line_is_unchanged_when_nothing_at_all_can_play()
    {
        var folder = Path.Combine("C:\\x", "assets", "audio");

        Assert.Contains(folder, AiCommandService.FormatFailedAudio(folder, hasFiles: false));
        Assert.Contains("output device", AiCommandService.FormatFailedAudio(folder, hasFiles: true));
    }

    [Fact]
    public void The_random_pick_plays_through_the_master_volume_sink_and_never_synthesises()
    {
        var src = SourceRoots.ReadProductFile("Services", "Commands", "MediaCommand.cs");
        // CCP.Core MediaCommand plays through the head's AudioSurface; WPF seeds it in App.AiEffects.cs.
        var surface = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "App.AiEffects.cs"));

        Assert.Contains("var (pool, candidates) = CurrentAudioPool();", src);
        Assert.Contains("AudioSurface is { } play && await play(path)", src);
        Assert.Contains("Audio?.PlaySound(path, 100)", surface);     // the sink that sits under the master dial
        Assert.DoesNotContain("SpeechSynthesizer", src);
        Assert.DoesNotContain("System.Speech", src);
    }

    [Fact]
    public void Panic_stops_the_one_shot_clip()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "App.xaml.cs"));

        var kill = app.IndexOf("public static void KillAllAudio()", StringComparison.Ordinal);
        Assert.True(kill >= 0);
        Assert.Contains("Audio?.StopSound();", app.Substring(kill, 900));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "MainWindow")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
