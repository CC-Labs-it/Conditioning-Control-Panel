using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public class VideoCutPlaybackTests
{
    [Theory]
    [InlineData(0, 600000)]
    [InlineData(30, 0)]
    [InlineData(30, 20000)]
    [InlineData(30, 30000)]
    public void Unknown_short_and_uncapped_clips_start_at_zero(int cap, long length)
        => Assert.Equal(0, VideoLengthCapRules.StartOffsetMs(cap, length, new Random(1)));

    [Fact]
    public void Random_slice_leaves_the_whole_cap_and_varies()
    {
        var rng = new Random(42);
        var offsets = new System.Collections.Generic.HashSet<long>();
        for (var i = 0; i < 100; i++)
        {
            var offset = VideoLengthCapRules.StartOffsetMs(30, 120000, rng);
            Assert.InRange(offset, 0, 90000);
            offsets.Add(offset);
        }
        Assert.True(offsets.Count > 90);
    }

    [Fact]
    public void Seeking_pausing_and_buffering_do_not_consume_the_cap_or_watch_credit()
    {
        var clock = new VideoPlaybackClock();
        clock.Observe(60000, 0); // initial random seek
        clock.Observe(61000, 1000);
        clock.Observe(61000, 20000); // buffering
        Assert.Equal(1000, clock.PlayedMs);
        clock.Observe(62000, 21000);
        clock.ResetPosition(); // grace resume or explicit seek
        clock.Observe(90000, 30000);
        clock.Observe(91000, 31000);
        Assert.Equal(3000, clock.PlayedMs);
        Assert.Null(MandatoryVideoScheduler.Guard(40, 40, true, true, 120, 0, 5, capPlayed: clock.PlayedMs / 1000.0));
        clock.Observe(93000, 33000);
        Assert.Equal("max length", MandatoryVideoScheduler.Guard(42, 42, true, true, 120, 0, 5, capPlayed: clock.PlayedMs / 1000.0));
        Assert.Equal("max length", MandatoryVideoScheduler.Guard(1, 1, true, true, 120, 0, 5, 4, 1)); // heal budget
    }
    [Fact]
    public void Repeating_position_counts_the_wrapped_tail_but_not_a_backward_seek()
    {
        var clock = new VideoPlaybackClock();
        clock.Observe(9000, 0, 10000);
        clock.Observe(500, 1500, 10000);
        Assert.Equal(1500, clock.PlayedMs);
        clock.Observe(400, 1600, 10000);
        Assert.Equal(1500, clock.PlayedMs);
    }
}
