using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AudioPlayer;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The audio player's playlist rules: order, loop, shuffle, removal, skipped files.</summary>
public class AudioPlaylistRulesTests
{
    private const string A = @"C:\music\a.mp3", B = @"C:\music\b.mp3", C = @"D:\other\a.mp3", D = @"C:\music\d.flac";

    private static AudioPlaylistRules List(params string[] paths)
    {
        var r = new AudioPlaylistRules(new Random(7));
        r.Add(paths);
        return r;
    }

    [Fact]
    public void Add_skips_a_path_already_in_the_list_and_keeps_same_named_files_apart()
    {
        var r = List(A, B);

        int added = r.Add(new[] { A, @"c:\MUSIC\B.mp3", C, "", "  ", C });

        Assert.Equal(1, added);                         // only C is new: same name as A, another folder
        Assert.Equal(new[] { A, B, C }, r.Tracks);
        Assert.Equal(-1, r.CurrentIndex);
    }

    [Fact]
    public void Next_walks_the_list_and_a_press_wraps_at_the_end()
    {
        var r = List(A, B, C);

        Assert.Equal(0, r.Advance(auto: false));        // nothing current yet: the first track
        Assert.Equal(1, r.Advance(auto: false));
        Assert.Equal(2, r.Advance(auto: false));
        Assert.Equal(0, r.Advance(auto: false));
    }

    [Fact]
    public void Previous_walks_back_and_wraps_at_the_top()
    {
        var r = List(A, B, C);
        r.Select(1);

        Assert.Equal(0, r.Back());
        Assert.Equal(2, r.Back());
    }

    [Fact]
    public void Loop_off_ends_after_the_last_track_and_rewinds_to_the_first()
    {
        var r = List(A, B);
        r.Select(1);

        Assert.Equal(-1, r.Advance(auto: true));
        Assert.Equal(0, r.CurrentIndex);
    }

    [Fact]
    public void Loop_all_carries_on_from_the_first_track()
    {
        var r = List(A, B);
        r.Loop = AudioPlaylistLoop.All;
        r.Select(1);

        Assert.Equal(0, r.Advance(auto: true));
    }

    [Fact]
    public void Loop_one_repeats_a_track_that_ended_but_a_press_on_next_still_moves()
    {
        var r = List(A, B);
        r.Loop = AudioPlaylistLoop.One;
        r.Select(0);

        Assert.Equal(0, r.Advance(auto: true));
        Assert.Equal(1, r.Advance(auto: false));
    }

    [Fact]
    public void Shuffle_plays_every_track_once_before_any_repeats()
    {
        var r = List(A, B, C, D);
        r.Shuffle = true;
        r.Loop = AudioPlaylistLoop.All;

        var round = new List<int>();
        for (int i = 0; i < 4; i++) round.Add(r.Advance(auto: true));

        Assert.Equal(new[] { 0, 1, 2, 3 }, round.OrderBy(i => i));
        int last = round[^1];
        Assert.NotEqual(last, r.Advance(auto: true));   // a new round never opens on the track just heard
    }

    [Fact]
    public void Shuffle_with_loop_off_ends_when_the_round_is_played()
    {
        var r = List(A, B, C);
        r.Shuffle = true;
        for (int i = 0; i < 3; i++) Assert.True(r.Advance(auto: true) >= 0);

        Assert.Equal(-1, r.Advance(auto: true));
        Assert.True(r.Advance(auto: false) >= 0);       // a press starts the next round
    }

    [Fact]
    public void Shuffle_previous_returns_to_the_track_heard_before()
    {
        var r = List(A, B, C, D);
        r.Shuffle = true;
        int first = r.Advance(auto: false);
        r.Advance(auto: false);

        Assert.Equal(first, r.Back());
    }

    [Fact]
    public void Removing_the_current_track_hands_over_to_the_one_that_follows()
    {
        var r = List(A, B, C);
        r.Select(1);

        Assert.True(r.RemoveAt(1));

        Assert.Equal(1, r.CurrentIndex);
        Assert.Equal(C, r.CurrentPath);
    }

    [Fact]
    public void Removing_the_current_last_track_ends_unless_the_list_loops()
    {
        var r = List(A, B);
        r.Select(1);
        Assert.True(r.RemoveAt(1));
        Assert.Equal(-1, r.CurrentIndex);

        var looped = List(A, B);
        looped.Loop = AudioPlaylistLoop.All;
        looped.Select(1);
        Assert.True(looped.RemoveAt(1));
        Assert.Equal(A, looped.CurrentPath);

        var only = List(A);
        only.Loop = AudioPlaylistLoop.All;
        only.Select(0);
        Assert.True(only.RemoveAt(0));
        Assert.Equal(-1, only.CurrentIndex);
    }

    [Fact]
    public void Removing_another_track_keeps_the_current_one_current()
    {
        var r = List(A, B, C);
        r.Select(2);

        Assert.False(r.RemoveAt(0));
        Assert.Equal(C, r.CurrentPath);
        Assert.False(r.RemoveAt(5));
        Assert.Equal(new[] { B, C }, r.Tracks);
    }

    [Fact]
    public void Move_reorders_and_the_current_track_follows()
    {
        var r = List(A, B, C);
        r.Select(0);

        Assert.True(r.Move(0, 2));

        Assert.Equal(new[] { B, C, A }, r.Tracks);
        Assert.Equal(A, r.CurrentPath);
        Assert.False(r.Move(1, 1));
        Assert.False(r.Move(9, 0));
    }

    [Fact]
    public void A_missing_file_is_skipped_for_the_next_one_that_opens()
    {
        var r = List(A, B, C);
        var tried = new List<string>();

        int at = r.SelectPlayable(0, forward: true, auto: false, p => { tried.Add(p); return p == C; });

        Assert.Equal(2, at);
        Assert.Equal(C, r.CurrentPath);
        Assert.Equal(new[] { A, B, C }, tried);
    }

    [Fact]
    public void A_list_with_no_file_left_tries_each_track_once_and_gives_up()
    {
        var r = List(A, B, C);
        r.Loop = AudioPlaylistLoop.All;
        int tries = 0;

        Assert.Equal(-1, r.SelectPlayable(1, forward: true, auto: true, _ => { tries++; return false; }));
        Assert.Equal(3, tries);

        var one = List(A);
        one.Loop = AudioPlaylistLoop.One;
        one.Select(0);
        tries = 0;
        Assert.Equal(-1, one.SelectPlayable(0, forward: true, auto: true, _ => { tries++; return false; }));
        Assert.Equal(1, tries);
    }

    [Fact]
    public void A_missing_last_track_does_not_restart_a_list_that_does_not_loop()
    {
        var r = List(A, B);
        r.Select(0);

        int at = r.SelectPlayable(r.Advance(auto: true), forward: true, auto: true, p => p == A);

        Assert.Equal(-1, at);
    }

    [Fact]
    public void Clear_empties_the_list()
    {
        var r = List(A, B);
        r.Select(1);

        r.Clear();

        Assert.Empty(r.Tracks);
        Assert.Equal(-1, r.CurrentIndex);
        Assert.Equal(-1, r.Advance(auto: false));
        Assert.Equal(-1, r.Back());
    }

    [Theory]
    [InlineData(80, 100, 80)]
    [InlineData(80, 50, 40)]
    [InlineData(100, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(250, 100, 100)]
    public void Loudness_is_the_players_level_scaled_by_master_volume(int volume, int master, int expected)
        => Assert.Equal(expected, AudioPlaylistRules.EffectiveVolume(volume, master));

    [Fact]
    public void The_playlist_is_machine_local_never_in_a_backup_and_kept_across_a_restore()
    {
        foreach (var name in new[] { nameof(AppSettings.AudioPlaylistPaths), nameof(AppSettings.AudioPlaylistVolume),
                                     nameof(AppSettings.AudioPlaylistLoop), nameof(AppSettings.AudioPlaylistShuffle),
                                     nameof(AppSettings.AudioPlaylistEnhancements) })
            Assert.True(ProfileSyncService.IsExcludedFromBackup(name), name);

        var current = new AppSettings
        {
            AudioPlaylistPaths = new List<string> { A, B },
            AudioPlaylistVolume = 35,
            AudioPlaylistLoop = 2,
            AudioPlaylistShuffle = true,
            AudioPlaylistEnhancements = false,
        };
        var restored = new AppSettings();
        Assert.True(restored.AudioPlaylistEnhancements);   // on by default (owner, 7.1.8)

        ProfileSyncService.PreserveLocalOnlyFields(current, restored);

        Assert.Equal(new[] { A, B }, restored.AudioPlaylistPaths);
        Assert.NotSame(current.AudioPlaylistPaths, restored.AudioPlaylistPaths);
        Assert.Equal(35, restored.AudioPlaylistVolume);
        Assert.Equal(2, restored.AudioPlaylistLoop);
        Assert.True(restored.AudioPlaylistShuffle);
        Assert.False(restored.AudioPlaylistEnhancements);
    }

    [Fact]
    public void The_engines_stop_stops_the_player_and_its_start_never_touches_it()
    {
        string root = FindRepoRoot();
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "ConditioningControlPanel",
            "MainWindow", "MainWindow.StartStop.cs"));

        // Every ordinary stop (button, tray, session end, scheduler, remote) is StopEngine,
        // and StopEngine is only a re-entrancy guard around StopEngineCore.
        string stop = Body(src, "public void StopEngine()");
        Assert.Contains("StopEngineCore();", stop);
        string core = Body(src, "private void StopEngineCore()");
        Assert.Contains("App.StopAudioPlaylist();", core);
        // Early in the pass: before the video teardown, which pumps the dispatcher for a while.
        Assert.True(core.IndexOf("App.StopAudioPlaylist();", StringComparison.Ordinal)
                    < core.IndexOf("App.Video.Stop();", StringComparison.Ordinal));

        string start = Body(src, "public void StartEngine(bool systemInitiated = false, bool resumingSession = false)");
        Assert.DoesNotContain("AudioPlaylist", start);
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

    [Fact]
    public void Panic_and_the_stop_all_pass_reach_the_player()
    {
        string root = FindRepoRoot();
        string app = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "ConditioningControlPanel", "App.xaml.cs"));
        string main = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));

        int kill = app.IndexOf("public static void KillAllAudio()", StringComparison.Ordinal);
        Assert.True(kill >= 0);
        Assert.Contains("StopAudioPlaylist();", app.Substring(kill, 400));
        Assert.Contains("Step(\"audio playlist\", App.StopAudioPlaylist);", main);          // the panic pass
        Assert.Contains("try { App.StopAudioPlaylist(); }", main);                          // the watchdog fallback
    }

    private static string FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "MainWindow")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
