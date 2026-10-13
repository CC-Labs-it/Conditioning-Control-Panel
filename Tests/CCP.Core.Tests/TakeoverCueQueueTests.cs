using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The takeover cue's queue and timing (WPF Overlays/TakeoverAnnouncerOverlay.cs IN/HOLD/OUT,
/// BloomEnv, the impact shake), on a stepped clock.</summary>
public class TakeoverCueQueueTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Cues_queue_and_play_one_after_another_without_dropping()
    {
        var q = new TakeoverCueQueue();
        Assert.True(q.Enqueue("FLASH", Ms(0)));
        Assert.False(q.Enqueue("SPIRAL", Ms(10)));   // waits its turn
        Assert.False(q.Enqueue("MANTRA", Ms(20)));
        Assert.Equal(2, q.Pending);

        Assert.Equal(Ms(2099), q.Tick(Ms(2099)));
        Assert.Equal("FLASH", q.Current);
        Assert.Equal(TimeSpan.Zero, q.Tick(Ms(2100)));   // fade-out ended: next starts from 0
        Assert.Equal("SPIRAL", q.Current);
        Assert.Equal(TimeSpan.Zero, q.Tick(Ms(4200)));
        Assert.Equal("MANTRA", q.Current);
        Assert.Null(q.Tick(Ms(6300)));
        Assert.Null(q.Current);

        Assert.True(q.Enqueue("VIDEO", Ms(7000)));   // idle again: shows at once
    }

    [Fact]
    public void Blank_labels_are_ignored_and_clear_drops_everything()
    {
        var q = new TakeoverCueQueue();
        Assert.False(q.Enqueue(null, Ms(0)));
        Assert.False(q.Enqueue("  ", Ms(0)));
        Assert.Null(q.Current);

        q.Enqueue("FLASH", Ms(0));
        q.Enqueue("SPIRAL", Ms(0));
        q.Clear();
        Assert.Null(q.Current);
        Assert.Equal(0, q.Pending);
        Assert.Null(q.Tick(Ms(100)));
        Assert.True(q.Enqueue("VIDEO", Ms(200)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(75, 0.5)]
    [InlineData(150, 1)]
    [InlineData(1849, 1)]
    [InlineData(1975, 0.5)]
    [InlineData(2100, 0)]
    public void Opacity_fades_in_150_holds_1700_fades_out_250(double ms, double expected)
        => Assert.Equal(expected, TakeoverCueQueue.Opacity(Ms(ms)), 3);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.075, 0.5)]
    [InlineData(0.15, 1)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.85, 0)]
    [InlineData(0.9, 0)]
    public void Bloom_ramps_up_015_and_eases_down_070(double sec, float expected)
        => Assert.Equal(expected, TakeoverCueQueue.BloomEnvelope(sec), 3);

    [Theory]
    [InlineData(0, 3.5)]
    [InlineData(0.13, 1.75)]
    [InlineData(0.26, 0)]
    [InlineData(1, 0)]
    public void Shake_decays_from_35_to_still_at_026(double sec, double expected)
        => Assert.Equal(expected, TakeoverCueQueue.ShakeAmplitude(sec), 3);
}
