using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
public sealed class MantraChantLoopTests
{
    private sealed class Rig
    {
        public bool Muted, Available = true, PlayWorks = true;
        public int Gap = 5, Picks, Stops;
        public IReadOnlyList<string> Clips = new[] { "ask", "affirm" };
        public readonly List<(string Clip, long Generation)> Played = new();
        public readonly MantraChantLoop Loop;
        public Rig() => Loop = new(() => Available, () => { Picks++; return Clips; },
            () => Muted, () => Gap, (p, g) => { Played.Add((p, g)); return PlayWorks; }, () => Stops++);
        public void End(double seconds) => Loop.ClipEnded(Played[^1].Generation, TimeSpan.FromSeconds(seconds));
        public void Tick(double seconds) => Loop.Tick(TimeSpan.FromSeconds(seconds));
    }
    [Fact]
    public async System.Threading.Tasks.Task Emergency_cancel_returns_during_play_and_retires_the_late_voice()
    {
        using var entered = new System.Threading.ManualResetEventSlim();
        using var release = new System.Threading.ManualResetEventSlim();
        var stops = 0; var played = new List<(string Clip, long Generation)>();
        var loop = new MantraChantLoop(() => true, () => new[] { "ask", "affirm" }, () => false, () => 5,
            (p, g) => { played.Add((p, g)); entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return true; }, () => System.Threading.Interlocked.Increment(ref stops));
        var starting = System.Threading.Tasks.Task.Run(() => loop.Start(TimeSpan.Zero));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await System.Threading.Tasks.Task.Run(loop.StopEmergency).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(loop.IsRunning); Assert.Equal(1, stops);
        }
        finally { release.Set(); await starting.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(2, stops); // the voice returned after cancellation is also stopped
        loop.ClipEnded(played[0].Generation, TimeSpan.FromSeconds(10)); loop.Tick(TimeSpan.FromSeconds(100));
        Assert.Single(played);
        loop.Start(TimeSpan.FromSeconds(101));
        Assert.True(loop.IsRunning); Assert.Equal("ask", played[1].Clip); loop.Stop();
    }
    [Fact]
    public void Explicit_start_plays_pair_with_answer_beat_then_configured_gap()
    {
        var r = new Rig();
        r.Tick(100);
        Assert.Empty(r.Played); // never auto-start
        r.Loop.Start(TimeSpan.Zero);
        r.Loop.Start(TimeSpan.Zero); // idempotent
        Assert.Equal("ask", Assert.Single(r.Played).Clip);
        r.End(1);
        r.Tick(3.49);
        Assert.Single(r.Played);
        r.Tick(3.5);
        Assert.Equal("affirm", r.Played[1].Clip);
        r.Gap = 9; // gap setting read live at the end of the pair
        r.End(4);
        r.Tick(12.99);
        Assert.Equal(2, r.Played.Count);
        r.Tick(13);
        Assert.Equal("ask", r.Played[2].Clip);
    }
    [Fact]
    public void Mute_drops_partial_pair_and_unmute_starts_a_whole_mantra()
    {
        var r = new Rig();
        r.Loop.Start(TimeSpan.Zero);
        var stale = r.Played[0].Generation;
        r.Muted = true;
        r.Tick(.5);
        Assert.Equal(1, r.Stops);
        r.Loop.ClipEnded(stale, TimeSpan.FromSeconds(1));
        r.Tick(20);
        Assert.Single(r.Played);
        r.Muted = false;
        r.Tick(21);
        Assert.Equal("ask", r.Played[1].Clip);
        r.Loop.ClipEnded(stale, TimeSpan.FromSeconds(22));
        r.Tick(50);
        Assert.Equal(2, r.Played.Count); // stale completion cannot interrupt the new clip
    }
    [Fact]
    public void Starting_muted_opens_no_audio_and_stop_invalidates_late_callbacks()
    {
        var r = new Rig { Muted = true };
        r.Loop.Start(TimeSpan.Zero);
        Assert.True(r.Loop.IsRunning);
        Assert.Empty(r.Played);
        r.Muted = false;
        r.Tick(1);
        var stale = r.Played[0].Generation;
        r.Loop.Stop();
        r.Loop.ClipEnded(stale, TimeSpan.FromSeconds(2));
        r.Tick(100);
        Assert.False(r.Loop.IsRunning);
        Assert.Single(r.Played);
        r.Loop.Start(TimeSpan.FromSeconds(101));
        Assert.Equal("ask", r.Played[1].Clip);
    }
    [Fact]
    public void Text_only_content_is_bounded_and_missing_content_stays_off()
    {
        var r = new Rig { Available = false };
        r.Loop.Start(TimeSpan.Zero);
        Assert.False(r.Loop.IsRunning);
        Assert.Equal(0, r.Picks);
        r.Available = true;
        r.Clips = Array.Empty<string>();
        r.Loop.Start(TimeSpan.Zero);
        Assert.Equal(12, r.Picks);
        Assert.False(r.Loop.IsRunning);
    }
    [Fact]
    public void Failed_clip_waits_instead_of_spinning_and_gap_is_clamped()
    {
        var r = new Rig { PlayWorks = false, Gap = 100, Clips = new[] { "only" } };
        r.Loop.Start(TimeSpan.Zero);
        r.Tick(59.99);
        Assert.Single(r.Played);
        r.Tick(60);
        Assert.Equal(2, r.Played.Count);
    }
    [Theory]
    [InlineData(false, 50, 80, .4)] [InlineData(true, 100, 100, 0)] [InlineData(false, 200, 200, 1)] [InlineData(false, -10, 100, 0)]
    public void Live_volume_obeys_mute_master_and_clamp(bool muted, double chant, double master, double expected)
        => Assert.Equal(expected, MantraChantLoop.ResolveVolume(muted, chant, master), 8);
    [Theory]
    [InlineData("ask", "affirm", 2)] [InlineData(null, "affirm", 1)] [InlineData("ask", null, 1)] [InlineData(null, null, 0)]
    public void Available_halves_play_in_Wpf_order(string? prompt, string? response, int count)
    {
        var clips = MantraChantLoop.ResolveClips(prompt, response);
        Assert.Equal(count, clips.Count);
        if (count > 0) Assert.Equal(prompt ?? response, clips[0]);
    }
}
