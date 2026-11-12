using Avalonia;
using ConditioningControlPanel.Avalonia.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionDodgeTests
{
    [Fact]
    public void ClipsAndOffsetRangeKeepTheWholeControlInItsRegion()
    {
        Assert.True(DodgeRegion.IsStrip(new Rect(0, 36, 56, 865), 1563, 901));
        Assert.True(DodgeRegion.IsStrip(new Rect(0, 36, 236, 865), 1563, 901));
        Assert.True(DodgeRegion.IsStrip(new Rect(0, 0, 1563, 36), 1563, 901));
        Assert.False(DodgeRegion.IsStrip(new Rect(96, 36, 1400, 820), 1563, 901));
        Assert.False(DodgeRegion.IsStrip(default, 1563, 901));
        var home = new Rect(120, 100, 44, 44);
        var region = new Rect(0, 36, 56, 800);
        Assert.True(DodgeRegion.TryOffsetRange(home, region, out var minX, out var maxX, out var minY, out var maxY));
        Assert.True(maxX < 0);
        foreach (var (x, y) in new[] { (minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY) })
            Assert.True(DodgeRegion.Contains(region, new Rect(home.X + x, home.Y + y, home.Width, home.Height)));
        Assert.False(DodgeRegion.TryOffsetRange(home, new Rect(0, 0, 30, 800), out _, out _, out _, out _));
        Assert.False(DodgeRegion.TryOffsetRange(home, default, out _, out _, out _, out _));
        Assert.False(DodgeRegion.Contains(region, new Rect(30, 100, 44, 44)));
    }

    [Fact]
    public void PredictiveApproachStepsSidewaysInsideWindow()
    {
        var home = new Rect(200, 200, 50, 40);
        var region = new Rect(12, 12, 476, 376);
        Assert.True(DodgeRegion.TryStep(home, region, default, new Point(140, 220), new Vector(250, 0), 50, 80, out var step));
        Assert.Equal(0, step.X);
        Assert.Equal(50, step.Y);
        Assert.True(DodgeRegion.Contains(region, new Rect(home.X + step.X, home.Y + step.Y, home.Width, home.Height)));
        Assert.False(DodgeRegion.TryStep(home, region, default, new Point(140, 220), new Vector(100, 0), 50, 80, out _));
    }

    [Fact]
    public void ChromeBandConstrainsPredictedAndProximityStepsToItsRow()
    {
        var home = new Rect(440, 0, 46, 36);
        var band = new Rect(12, 0, 476, 36);
        Assert.True(DodgeRegion.TryOffsetRange(home, band, out _, out _, out var minY, out var maxY));
        Assert.Equal(0, minY);
        Assert.Equal(0, maxY);
        Assert.True(DodgeRegion.TryStep(home, band, default, new Point(400, 18), new Vector(220, 0), 50, 80, out var step));
        Assert.Equal(0, step.Y);
        Assert.True(DodgeRegion.Contains(band, new Rect(home.X + step.X, home.Y + step.Y, home.Width, home.Height)));
        Assert.True(DodgeRegion.TryStep(home, band, default, new Point(435, 18), default, 50, 80, out step));
        Assert.Equal(0, step.Y);
        Assert.True(step.X < 0);
    }

    [Fact]
    public void FullyConfinedControlDoesNotMove()
    {
        var home = new Rect(12, 12, 50, 40);
        Assert.False(DodgeRegion.TryStep(home, home, default, new Point(12, 20), default, 50, 80, out _));
    }
}
