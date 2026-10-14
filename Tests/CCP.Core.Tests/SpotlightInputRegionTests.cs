using System;
using System.Linq;
using ConditioningControlPanel.Tutorial;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SpotlightInputRegionTests
{
    [Theory]
    [InlineData(20, 10, 30, 40)]
    [InlineData(-10, -10, 30, 40)]
    [InlineData(80, 70, 40, 50)]
    [InlineData(-10, -10, 120, 120)]
    [InlineData(110, 10, 30, 40)]
    public void RegionCoversExactlyWindowMinusHole(int x, int y, int width, int height)
    {
        var rects = SpotlightInputRegion.Create(100, 100, 1, 1, x, y, width, height);
        Assert.All(rects, r => Assert.True(r.Width > 0 && r.Height > 0));
        for (int py = 0; py < 100; py++)
        for (int px = 0; px < 100; px++)
        {
            int hits = rects.Count(r => px >= r.X && px < r.X + r.Width && py >= r.Y && py < r.Y + r.Height);
            bool hole = px >= x && px < x + width && py >= y && py < y + height;
            Assert.Equal(hole ? 0 : 1, hits);
        }
    }

    [Fact]
    public void FractionalDpiRoundsEdgesAsWpfAndKeepsHalfOpenHole()
    {
        var r = SpotlightInputRegion.Create(100, 80, 1.25, 1.5, 10.4, 20.2, 30.4, 10.2);
        Assert.Equal(new[] {
            new SpotlightInputRegion.Rect(0, 0, 125, 30),
            new SpotlightInputRegion.Rect(0, 46, 125, 74),
            new SpotlightInputRegion.Rect(0, 30, 13, 16),
            new SpotlightInputRegion.Rect(51, 30, 74, 16),
        }, r);
    }

    [Fact]
    public void InvalidOrUnmeasuredBoundsNeverProduceMalformedNativeRectangles()
    {
        Assert.Empty(SpotlightInputRegion.Create(double.NaN, 80, 1, 1, 0, 0, 1, 1));
        Assert.Equal(new[] { new SpotlightInputRegion.Rect(0, 0, 100, 80) },
            SpotlightInputRegion.Create(100, 80, 0, double.NaN, 10, 10, 0, 20));
    }
}
