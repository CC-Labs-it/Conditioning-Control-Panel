using System;
using ConditioningControlPanel.Services.Billboard.Motion;
using Xunit;
namespace CCP.Core.Tests;
public sealed class PosterMotionTests
{
    [Theory]
    [InlineData(1600, 900)]
    [InlineData(900, 1600)]
    [InlineData(2400, 180)]
    public void EveryPoseCoversTheCard(double w, double h)
    {
        for (int tick = 0; tick < 240; tick++)
        foreach (double energy in new[] { 0d, .5, 1 })
        foreach (double pointer in new[] { -1d, 0, 1 })
        {
            var pose = PosterMotion.At(tick * Math.Tau / 240, (pointer, -pointer), energy,
                PosterMotion.Land(tick / 30d), w, h);
            Assert.True(PosterMotion.Covers(pose, w, h), $"uncovered at {tick}, {energy}, {pointer}");
            Assert.InRange(pose.Scale, 1.03, 1.05);
        }
    }
    [Fact]
    public void BreathPanAndPointerAreVisibleButStayWithinTheCropBudget()
    {
        var first = PosterMotion.At(0, (0, 0), 0, 0, 1600, 900);
        var second = PosterMotion.At(Math.PI, (0, 0), 0, 0, 1600, 900);
        Assert.Equal(.005, second.Scale - first.Scale, 8);
        Assert.NotEqual(first.X, second.X);
        Assert.NotEqual(first.Y, second.Y);
        var hover = PosterMotion.At(0, (1, -1), 1, 0, 1600, 900);
        Assert.Equal(-8, hover.X - first.X, 8);
        Assert.Equal(3.6, hover.Y - first.Y, 8);
        Assert.NotEqual(first.SkewX, hover.SkewX);
        Assert.NotEqual(first.SkewY, hover.SkewY);
        Assert.Equal(1.034, PosterMotion.Still.Scale);
        Assert.Equal(.01, PosterMotion.Land(0));
        Assert.Equal(0, PosterMotion.Land(PosterMotion.LandSeconds));
    }
}
