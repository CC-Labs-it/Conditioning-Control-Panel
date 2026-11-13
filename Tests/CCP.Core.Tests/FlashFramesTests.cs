using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The animated flash decode rules, against WPF AnimatedWebp.DecodeFramesCore and
/// FlashService.ComputeDecodeMaxDim.</summary>
public class FlashFramesTests
{
    [Fact]
    public void A_heavy_clip_keeps_at_least_ten_frames()   // WPF AnimatedWebp.cs:184-187
    {
        // 1000x1000 = 3.8 MB a frame: 30 MB holds 7 of 40, WPF keeps 10 (stride 4), not 7.
        var p = FlashFrames.Plan(1000, 1000, 40, 1000);
        Assert.Equal((1000, 1000, 10, 4), (p.Width, p.Height, p.Keep, p.Step));
        // A light clip keeps every frame up to 60.
        Assert.Equal(60, FlashFrames.Plan(10, 10, 90, 10).Keep);
        Assert.Equal(2, FlashFrames.Plan(10, 10, 90, 10).Step);
    }

    [Fact]
    public void The_decode_is_the_display_size_but_never_above_2048()   // WPF FlashService.cs:1489-1494
    {
        var p = FlashFrames.Plan(4000, 1000, 2, 3000);
        Assert.Equal((2048, 512), (p.Width, p.Height));
        Assert.Equal((2048, 512), (FlashFrames.Plan(4000, 1000, 2, 0).Width, FlashFrames.Plan(4000, 1000, 2, 0).Height));
        Assert.Equal((400, 100), (FlashFrames.Plan(4000, 1000, 2, 400).Width, FlashFrames.Plan(4000, 1000, 2, 400).Height));
        Assert.Equal((300, 200), (FlashFrames.Plan(300, 200, 2, 3000).Width, FlashFrames.Plan(300, 200, 2, 3000).Height));   // never upscaled
    }

    [Fact]
    public void Only_the_first_600_frames_are_decoded()   // WPF AnimatedWebp.cs:89
    {
        var p = FlashFrames.Plan(10, 10, 1000, 10);
        Assert.Equal((60, 17, 600), (p.Keep, p.Step, p.DecodeCount));   // the stride still spans all 1000
        Assert.Equal(30, FlashFrames.Plan(10, 10, 30, 10).DecodeCount);
    }

    [Fact]
    public void The_delay_is_the_kept_frames_average_times_the_stride()   // WPF AnimatedWebp.cs:242-244
    {
        Assert.Equal(120, FlashFrames.DelayMs(20 + 100, 2, 2));    // average 60 x 2
        Assert.Equal(100, FlashFrames.DelayMs(0, 0, 1));            // no delays -> 100
        Assert.Equal(200, FlashFrames.DelayMs(10 * 3, 3, 2));       // average 10 < 20 -> 100, x 2
        Assert.Equal(2000, FlashFrames.DelayMs(700, 1, 4));         // held to 2000
        Assert.Equal(600, FlashFrames.DelayMs(600, 1, 1));          // above the spiral's 500 -> kept
    }
}
