using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1414: sounds with the engine off. The guard (keyword triggers hold their audio) and
/// the log (one line per player per ten seconds, file name only).
/// </summary>
public class EngineOffAudioTests
{
    private static readonly DateTime T0 = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void A_keyword_trigger_holds_its_audio_only_while_nothing_runs(bool engine, bool session, bool held)
    {
        Assert.Equal(held, KeywordTriggerService.HoldsAudio(engine, session));
    }

    [Fact]
    public void Every_keyword_audio_path_asks_the_guard_and_the_test_button_does_not()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "KeywordTriggerService.cs"));

        Assert.Contains("if (AudioHeldForEngineOff(trigger, \"PlayAudio\")) return 0;", src);
        Assert.Contains("&& !AudioHeldForEngineOff(trigger, \"legacy audio\"))", src);
        Assert.Contains("&& !AudioHeldForEngineOff(trigger, \"MindWipe\"))", src);

        // The preset dialog's Test button plays, and is marked as asked for so it is never logged.
        Assert.Contains("return PlayTriggerAudio(resolved, volumePercent, asked: true);", src);
        Assert.Contains("EngineOffAudioLog.Note(\"keyword-trigger\", path, asked);", src);
    }

    [Fact]
    public void The_line_names_the_player_and_the_file_name_never_the_path()
    {
        var line = EngineOffAudioLog.LineFor("test-name-" + Guid.NewGuid(), @"C:\Users\someone\assets\mindwipe\wand 01.mp3",
            engineRunning: false, asked: false, T0);

        Assert.NotNull(line);
        Assert.Contains("started 'wand 01.mp3'", line);
        Assert.StartsWith("engine off: test-name-", line);
        Assert.DoesNotContain("someone", line);
        Assert.Equal("?", EngineOffAudioLog.NameOf(null));
    }

    [Fact]
    public void Nothing_is_written_while_the_engine_runs_or_for_a_sound_the_player_asked_for()
    {
        var player = "test-quiet-" + Guid.NewGuid();
        Assert.Null(EngineOffAudioLog.LineFor(player, "a.mp3", engineRunning: true, asked: false, T0));
        Assert.Null(EngineOffAudioLog.LineFor(player, "a.mp3", engineRunning: false, asked: true, T0));
        // Neither of those spent the player's slot.
        Assert.NotNull(EngineOffAudioLog.LineFor(player, "a.mp3", engineRunning: false, asked: false, T0));
    }

    [Fact]
    public void One_line_per_player_per_ten_seconds()
    {
        var a = "test-rate-a-" + Guid.NewGuid();
        var b = "test-rate-b-" + Guid.NewGuid();

        Assert.NotNull(EngineOffAudioLog.LineFor(a, "1.mp3", false, false, T0));
        Assert.Null(EngineOffAudioLog.LineFor(a, "2.mp3", false, false, T0.AddSeconds(9)));
        Assert.NotNull(EngineOffAudioLog.LineFor(b, "1.mp3", false, false, T0.AddSeconds(9)));
        Assert.NotNull(EngineOffAudioLog.LineFor(a, "3.mp3", false, false, T0.AddSeconds(10)));
    }

    [Theory]
    [InlineData("Services/LockCard/MindWipeService.cs", "EngineOffAudioLog.Note(\"mindwipe\"")]
    [InlineData("Services/LockCard/MindWipeService.cs", "EngineOffAudioLog.Note(\"mindwipe-loop\"")]
    [InlineData("Services/LockCard/BrainDrainService.cs", "EngineOffAudioLog.Note(\"braindrain\"")]
    [InlineData("Services/Flash/FlashService.cs", "EngineOffAudioLog.Note(\"flash-voice\"")]
    [InlineData("Services/Audio/AudioService.Playback.cs", "EngineOffAudioLog.Note(\"oneshot:\" + tag")]
    [InlineData("Services/AudioService.cs", "EngineOffAudioLog.Note(\"sound\"")]
    [InlineData("Services/MantraChantService.cs", "EngineOffAudioLog.Note(\"mantra-chant\"")]
    [InlineData("Services/Video/VideoService.cs", "EngineOffAudioLog.Note(\"video\"")]
    public void Each_covered_player_reports_its_starts(string file, string call)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", file.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(call, src);
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
