using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Tests;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.AudioPlayer;
using Xunit;
using WpfAudio = ConditioningControlPanel.Services.Audio.EngineOffAudioLog;
namespace CCP.Core.Tests;
public class Sync7AudioTests
{
    [Fact]
    public void WpfFacadeSharesTheCoreBudgetAndReset()
    {
        var player = Guid.NewGuid().ToString();
        var now = new DateTime(2026, 11, 1);
        try
        {
            Assert.NotNull(WpfAudio.LineFor(player, "/private/a.wav", false, false, now));
            Assert.Null(CoreAudioDiagnostics.LineFor(player, "b.wav", false, false, now.AddSeconds(1)));
            Assert.NotNull(CoreAudioDiagnostics.LineFor(player + "2", "a.wav", false, false, now));
            Assert.Null(WpfAudio.LineFor(player + "2", "b.wav", false, false, now.AddSeconds(1)));
            WpfAudio.ResetForTests();
            Assert.NotNull(CoreAudioDiagnostics.LineFor(player, "b.wav", false, false, now.AddSeconds(1)));
            Assert.Equal(CoreAudioDiagnostics.NameOf(@"C:\private\a.wav"), WpfAudio.NameOf(@"C:\private\a.wav"));
        }
        finally { CoreAudioDiagnostics.ResetForTests(); }
    }
    [Fact]
    public void HeldLinesKeepTheirOwnSharedSlotAndWpfWording()
    {
        var player = Guid.NewGuid().ToString();
        var now = new DateTime(2026, 11, 1);
        Assert.NotNull(CoreAudioDiagnostics.LineFor(player, "a.wav", false, false, now));
        Assert.Equal($"engine off: {player} held its MindWipe", CoreAudioDiagnostics.HeldLineFor(player, "MindWipe", now));
        Assert.Null(CoreAudioDiagnostics.HeldLineFor(player, "audio", now.AddSeconds(9)));
        Assert.NotNull(CoreAudioDiagnostics.HeldLineFor(player, "audio", now.AddSeconds(10)));
    }
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void WpfAndCoreUseTheSameRunningRule(bool engine, bool session, bool running)
    {
        Assert.Equal(running, WpfAudio.IsRunning(engine, session));
        Assert.Equal(running, CoreAudioDiagnostics.IsRunning(engine, session));
    }
    [Fact]
    public void PlaylistRulesHaveOnlyOneProductSource()
    {
        var source = Assert.Single(SourceRoots.EnumerateProductSources("AudioPlaylistRules.cs"));
        Assert.Equal(Path.Combine(SourceRoots.RepoRoot, "CCP.Core", "Services", "AudioPlayer", "AudioPlaylistRules.cs"), source);
    }
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
        Assert.Equal("clip.wav", CoreAudioDiagnostics.NameOf("C:clip.wav"));
        Assert.Equal("clip.wav", WpfAudio.NameOf("C:clip.wav"));
        Assert.Equal("?", CoreAudioDiagnostics.NameOf("z:"));
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
