using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VideoRevealTests
{
    [Fact]
    public void One_decoded_frame_starts_a_continuous_fade_and_controls_wait_for_completion()
    {
        var frames = new Queue<Action>();
        double now = 10, opacity = -1;
        var controls = 0;
        using var reveal = new VideoReveal(0.75, frames.Enqueue, x => opacity = x, () => controls++, () => now);
        reveal.Start(); // the only decoded frame
        Assert.Equal(0, opacity);
        Assert.Equal(0, controls);
        now += 0.375;
        frames.Dequeue()(); // UI frame, no decoded frame
        Assert.Equal(0.5, opacity);
        Assert.Equal(0, controls);
        now += 0.375;
        frames.Dequeue()();
        Assert.Equal(1, opacity);
        Assert.Equal(1, controls);
        Assert.Empty(frames);
        reveal.Start(); // duplicate notification cannot arm another reveal
        Assert.Equal(1, controls);
    }

    [Fact]
    public void Close_or_replay_cancels_queued_opacity_and_controls_work()
    {
        var frames = new Queue<Action>();
        double now = 0, opacity = -1;
        var controls = 0;
        var old = new VideoReveal(0.75, frames.Enqueue, x => opacity = x, () => controls++, () => now);
        old.Start();
        old.Dispose(); // close, or replay before the next UI frame
        now = 2;
        using var next = new VideoReveal(0.75, frames.Enqueue, x => opacity = x, () => controls++, () => now);
        next.Start();
        frames.Dequeue()(); // old frame cannot reveal the replacement clip
        Assert.Equal(0, opacity);
        Assert.Equal(0, controls);
        now += 0.75;
        frames.Dequeue()();
        Assert.Equal(1, opacity);
        Assert.Equal(1, controls);
        Assert.Empty(frames);
    }

    [Fact]
    public void Without_a_fade_picture_and_controls_show_immediately()
    {
        double opacity = -1;
        var controls = 0;
        using var reveal = new VideoReveal(0, _ => Assert.Fail("No animation needed"),
            x => opacity = x, () => controls++);
        reveal.Start();
        Assert.Equal(1, opacity);
        Assert.Equal(1, controls);
    }
}
