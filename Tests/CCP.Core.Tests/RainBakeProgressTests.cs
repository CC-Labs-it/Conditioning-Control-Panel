using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RainBakeProgressTests
{
    [Theory]
    [InlineData(0, 17, 0, 0, 1)]
    [InlineData(3, 17, 0.5, 3.5 / 17, 4)]
    [InlineData(17, 17, 0.7, 1, 17)]
    [InlineData(20, 17, 0, 1, 17)]
    [InlineData(2, 4, 3, 0.75, 3)]
    [InlineData(0, 0, 0.5, 0, 0)]
    public void Counts_finished_clips_and_clamps_stale_frame_progress(
        int done, int total, double clip, double fraction, int shown)
    {
        var progress = new RainBakeProgress(done, total, clip);
        Assert.Equal(fraction, progress.Fraction, 6);
        Assert.Equal(shown, progress.ClipShown);
        Assert.Equal((int)System.Math.Floor(fraction * 100), progress.Percent);
    }

    [Fact]
    public void Finishing_a_clip_preserves_progress_and_advances_the_label()
    {
        var before = new RainBakeProgress(2, 17, 1);
        var after = new RainBakeProgress(3, 17, 0);
        Assert.Equal(before.Fraction, after.Fraction);
        Assert.Equal("Clip 3 / 17", before.Fill("Clip {n} / {total}"));
        Assert.Equal("Clip 4 / 17", after.Fill("Clip {n} / {total}"));
    }
}
