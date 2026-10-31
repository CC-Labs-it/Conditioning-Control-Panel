using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.AudioPlayer;
using Xunit;

namespace CCP.Core.Tests;

public class Sync7AudioTests
{
    [Fact]
    public void DiagnosticsArePrivateAndRateLimitedPerPlayer()
    {
        var player = Guid.NewGuid().ToString();
        var now = new DateTime(2026, 10, 15);
        Assert.Null(CoreAudioDiagnostics.LineFor(player, "secret", true, false, now));
        Assert.Null(CoreAudioDiagnostics.LineFor(player, "secret", false, true, now));
        Assert.Equal($"engine off: {player} started 'clip.wav'",
            CoreAudioDiagnostics.LineFor(player, @"C:\Users\private\clip.wav", false, false, now));
        Assert.Null(CoreAudioDiagnostics.LineFor(player, "clip.wav", false, false, now.AddSeconds(9)));
        Assert.NotNull(CoreAudioDiagnostics.LineFor(player + "2", "clip.wav", false, false, now));
        Assert.NotNull(CoreAudioDiagnostics.LineFor(player, "clip.wav", false, false, now.AddSeconds(10)));
        Assert.NotNull(CoreAudioDiagnostics.LineFor(player, "clip.wav", false, false, now.AddSeconds(-1)));
        Assert.Equal("clip.wav", CoreAudioDiagnostics.NameOf("/private/clip.wav"));
        Assert.Equal("?", CoreAudioDiagnostics.NameOf(null));
    }

    [Fact]
    public void PlaylistEditsPreserveTheSelectedTrackAndIgnoreDuplicatePaths()
    {
        var r = new AudioPlaylistRules();
        Assert.Equal(3, r.Add(new[] { " a ", "A", "", "b", "c" }));
        r.Select(1);
        Assert.True(r.Move(1, 0));
        Assert.Equal("b", r.CurrentPath);
        Assert.False(r.RemoveAt(2));
        Assert.Equal("b", r.CurrentPath);
        Assert.True(r.RemoveAt(0));
        Assert.Equal("a", r.CurrentPath);
        r.Clear();
        Assert.Equal(-1, r.CurrentIndex);
        Assert.Empty(r.Tracks);
    }

    [Fact]
    public void AutomaticEndStopsButManualNextWrapsAndLoopOneOnlyRepeatsAutomatically()
    {
        var r = new AudioPlaylistRules();
        r.Add(new[] { "a", "b" }); r.Select(1);
        Assert.Equal(-1, r.Advance(auto: true));
        Assert.Equal(0, r.CurrentIndex);
        r.Select(1);
        Assert.Equal(0, r.Advance(auto: false));
        r.Loop = AudioPlaylistLoop.One;
        Assert.Equal(0, r.Advance(auto: true));
        Assert.Equal(1, r.Advance(auto: false));
        r.Loop = AudioPlaylistLoop.All;
        Assert.Equal(0, r.Advance(auto: true));
        Assert.Equal(25, AudioPlaylistRules.EffectiveVolume(50, 50));
        Assert.Equal(100, AudioPlaylistRules.EffectiveVolume(200, 200));
    }

    [Fact]
    public void ShuffleVisitsEachTrackOnceAndBackUsesHistory()
    {
        var r = new AudioPlaylistRules(new Random(7));
        r.Add(new[] { "a", "b", "c" }); r.Select(0); r.Shuffle = true;
        var seen = new HashSet<string> { r.CurrentPath! };
        r.Advance(false); var previous = r.CurrentPath;
        Assert.True(seen.Add(previous!));
        r.Advance(false); Assert.True(seen.Add(r.CurrentPath!));
        r.Back(); Assert.Equal(previous, r.CurrentPath);
        var attempts = 0;
        r.Loop = AudioPlaylistLoop.One;
        Assert.Equal(-1, r.SelectPlayable(r.CurrentIndex, true, true, _ => { attempts++; return false; }));
        Assert.InRange(attempts, 1, 3);
    }
}
