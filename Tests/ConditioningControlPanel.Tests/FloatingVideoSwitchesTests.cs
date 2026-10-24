using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The three Video page switches promised on 2026-10-20: "Pop up together", "Keep sound on the
/// video I'm watching" and "Replay after a failed check". Defaults keep today's behaviour.
/// </summary>
public class FloatingVideoSwitchesTests
{
    // ---- defaults, and a settings file without the keys ----

    [Fact]
    public void Defaults_keep_todays_behaviour()
    {
        var s = new AppSettings();
        Assert.False(s.MandatoryVideoFloatingTogether);
        Assert.False(s.MandatoryVideoFloatingKeepSound);
        Assert.True(s.MandatoryVideoReplayAfterFailedCheck);

        var loaded = JsonConvert.DeserializeObject<AppSettings>("{}",
            new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace })!;
        Assert.False(loaded.MandatoryVideoFloatingTogether);
        Assert.False(loaded.MandatoryVideoFloatingKeepSound);
        Assert.True(loaded.MandatoryVideoReplayAfterFailedCheck);
    }

    [Fact]
    public void The_switches_survive_a_round_trip()
    {
        var s = new AppSettings
        {
            MandatoryVideoFloatingTogether = true,
            MandatoryVideoFloatingKeepSound = true,
            MandatoryVideoReplayAfterFailedCheck = false,
        };
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.True(back.MandatoryVideoFloatingTogether);
        Assert.True(back.MandatoryVideoFloatingKeepSound);
        Assert.False(back.MandatoryVideoReplayAfterFailedCheck);
    }

    // ---- Pop up together ----

    [Theory]
    [InlineData(0, 4, false, 1)]
    [InlineData(2, 4, false, 1)]
    [InlineData(4, 4, false, 0)]
    [InlineData(0, 4, true, 4)]
    [InlineData(1, 4, true, 3)]
    [InlineData(3, 4, true, 1)]
    [InlineData(4, 4, true, 0)]
    [InlineData(0, 1, true, 1)]
    [InlineData(0, 9, true, 4)]   // the limit is clamped to MaxWindows
    public void A_trigger_opens_one_window_or_every_free_slot_together(int up, int limit, bool together, int expected)
        => Assert.Equal(expected, MandatoryVideoPresentation.WindowsToOpen(up, limit, together));

    [Fact]
    public void Nothing_joins_a_run_that_opened_together()
    {
        Assert.NotNull(MandatoryVideoPresentation.JoinDelaySeconds(12, 4, 0.5));
        Assert.Null(MandatoryVideoPresentation.JoinDelaySeconds(12, 4, 0.5, together: true));
    }

    // ---- Keep sound on the video I'm watching ----

    [Fact]
    public void Keep_sound_off_the_newest_window_takes_the_sound()
    {
        var f = new FloatingAudioFocus<string>();
        f.Join("a", keepHolder: false);
        f.Join("b", keepHolder: false);
        Assert.Equal("b", f.Holder);
    }

    [Fact]
    public void Keep_sound_on_a_new_window_joins_muted_and_the_watched_one_keeps_it()
    {
        var f = new FloatingAudioFocus<string>();
        f.Join("a", keepHolder: true);          // nothing playing: the first one takes the sound
        Assert.Equal("a", f.Holder);
        f.Join("b", keepHolder: true);
        f.Join("c", keepHolder: true);
        Assert.Equal("a", f.Holder);
        Assert.False(f.HasSound("b"));
        Assert.False(f.HasSound("c"));

        // A touch still moves the sound.
        Assert.True(f.Touch("b"));
        Assert.Equal("b", f.Holder);

        // The holder closes: the most recent survivor takes it, never nobody.
        Assert.True(f.Remove("b"));
        Assert.NotNull(f.Holder);
        Assert.Equal(2, f.Count);
    }

    [Fact]
    public void Keep_sound_rejoining_the_holder_changes_nothing()
    {
        var f = new FloatingAudioFocus<string>();
        f.Join("a", keepHolder: true);
        f.Join("b", keepHolder: true);
        f.Join("a", keepHolder: true);
        Assert.Equal("a", f.Holder);
        Assert.Equal(2, f.Count);
    }

    // ---- Replay after a failed check ----

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, true)]    // a strict video always replays
    [InlineData(false, false, true, true)]    // so does a Lockdown
    [InlineData(false, true, true, true)]
    public void Replay_off_ends_an_ordinary_video_but_never_a_strict_one_or_a_Lockdown(
        bool setting, bool strict, bool lockdown, bool expected)
        => Assert.Equal(expected, MandatoryVideoPresentation.ReplaysAfterCheck(setting, strict, lockdown));

    [Fact]
    public void The_verdict_is_counted_before_the_replay_is_dropped()
    {
        var main = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "VideoService.cs"));
        var fail = main.IndexOf("App.Achievements?.TrackAttentionCheckFailed();", StringComparison.Ordinal);
        var rule = main.IndexOf("MandatoryVideoPresentation.ReplaysAfterCheck(", StringComparison.Ordinal);
        var replay = main.IndexOf("if (loop && !string.IsNullOrEmpty(_retryPath))", StringComparison.Ordinal);
        Assert.True(fail >= 0 && rule > fail && replay > rule);
        Assert.Contains("settings.MandatoryVideoReplayAfterFailedCheck, _strictActive, App.Lockdown?.IsActive == true", main);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
