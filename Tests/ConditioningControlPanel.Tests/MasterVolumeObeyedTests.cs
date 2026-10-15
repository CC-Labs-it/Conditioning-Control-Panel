using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1432: the log said "MasterVolume is 0% - ALL audio will be silent" and then
/// "Loop started with mindwipe.mp3 at 100% volume". Mind Wipe never read the master dial, and the
/// plain PlaySound path (the companion's Audio effect, Deeper's play-audio action) floored at 5%.
/// ccp-bugs #1428: the Audio effect's failure line named a folder no page shows.</summary>
public class MasterVolumeObeyedTests
{
    // ---- Mind Wipe ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1.0, 0f)]        // the report: master 0%, Mind Wipe 100%
    [InlineData(100, 1.0, 1f)]
    [InlineData(50, 1.0, 0.5f)]
    [InlineData(100, 0.6, 0.6f)]
    [InlineData(50, 0.5, 0.25f)]
    [InlineData(80, 0.0, 0f)]
    [InlineData(150, 2.0, 1f)]
    [InlineData(-5, 0.5, 0f)]
    public void Mind_wipe_gain_is_master_times_own(int master, double own, float expected)
        => Assert.Equal(expected, MindWipeService.EffectiveVolume(master, own), 3);

    [Fact]
    public void Mind_wipe_hands_no_player_its_own_level_alone()
    {
        // Every reader in the service takes CurrentGain (master x own). A reader given the bare
        // "_volume" is the bug coming back on one of the five paths (one-shot, loop A/B, parked,
        // live update).
        var source = File.ReadAllText(Path.Combine(AppDir(), "Services", "LockCard", "MindWipeService.cs"));
        Assert.DoesNotContain("(float)_volume", source);
        Assert.Contains("MasterVolumeWatch.Shared.Changed += OnMasterVolumeChanged", source);
    }

    // ---- the plain PlaySound path ------------------------------------------------------------

    [Theory]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(0, 0)]
    public void Play_sound_is_silent_when_either_dial_is_at_zero(int own, int master)
        => Assert.Equal(0f, AudioService.SoundGain(own, master));

    [Fact]
    public void Play_sound_sits_under_the_master_dial()
    {
        Assert.Equal(1f, AudioService.SoundGain(100, 100), 3);
        Assert.True(AudioService.SoundGain(100, 50) < AudioService.SoundGain(100, 100));
        Assert.Equal(AudioService.SoundGain(50, 100), AudioService.SoundGain(100, 50), 3);
        // A quiet but non-zero setting keeps its audible floor.
        Assert.Equal(0.05f, AudioService.SoundGain(100, 1), 3);
    }

    // ---- the dial is followed live, whoever moves it -----------------------------------------

    [Fact]
    public void A_master_volume_change_reaches_listeners_whoever_wrote_it()
    {
        var watch = new MasterVolumeWatch();
        var settings = new AppSettings { MasterVolume = 80 };
        var heard = new List<int>();
        watch.Changed += heard.Add;
        watch.Hook(settings);

        settings.MasterVolume = 0;       // a preset, a hotkey, the launcher dial: no slider handler
        settings.VideoVolume = 10;       // another setting is not a master change
        settings.MasterVolume = 35;

        Assert.Equal(new[] { 0, 35 }, heard);
    }

    [Fact]
    public void The_watch_follows_a_replaced_settings_object_and_drops_the_old_one()
    {
        var watch = new MasterVolumeWatch();
        var before = new AppSettings();
        var after = new AppSettings();
        var heard = new List<int>();
        watch.Changed += heard.Add;

        watch.Hook(before);
        watch.Hook(after);
        before.MasterVolume = 5;
        after.MasterVolume = 60;

        Assert.Equal(new[] { 60 }, heard);
    }

    [Fact]
    public void One_listener_throwing_does_not_stop_the_others()
    {
        var watch = new MasterVolumeWatch();
        var settings = new AppSettings();
        var heard = new List<int>();
        watch.Changed += _ => throw new System.InvalidOperationException("disposed reader");
        watch.Changed += heard.Add;
        watch.Hook(settings);

        settings.MasterVolume = 12;

        Assert.Equal(new[] { 12 }, heard);
    }

    // ---- #1428: the Audio effect's failure line ----------------------------------------------

    [Fact]
    public void An_empty_audio_folder_is_named_by_its_real_path()
    {
        var folder = Path.Combine("C:\\Users\\someone\\AppData\\Local\\CCP", "assets", "audio");
        var line = AiCommandService.FormatFailedAudio(folder, hasFiles: false);

        Assert.Contains(folder, line);
        Assert.Contains("your own files", line);
        Assert.DoesNotContain("assets audio folder", line);
    }

    [Fact]
    public void Files_that_are_there_but_did_not_start_do_not_blame_the_folder()
    {
        var line = AiCommandService.FormatFailedAudio("C:\\x\\assets\\audio", hasFiles: true);

        Assert.DoesNotContain("C:\\x\\assets\\audio", line);
        Assert.Contains("output device", line);
    }

    [Fact]
    public void The_audio_folder_scan_counts_audio_only_and_looks_in_subfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-audio-scan-" + Path.GetRandomFileName());
        try
        {
            var folder = MediaCommand.AudioFolderUnder(root);
            Assert.Empty(MediaCommand.AudioFilesIn(folder));              // missing folder

            Directory.CreateDirectory(Path.Combine(folder, "set"));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");
            Assert.Empty(MediaCommand.AudioFilesIn(folder));              // the scaffolded, empty state

            File.WriteAllText(Path.Combine(folder, "set", "a.MP3"), "x");
            Assert.Single(MediaCommand.AudioFilesIn(folder));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + System.AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
