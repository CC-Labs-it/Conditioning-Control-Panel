using System;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ChaosDvdFlock against WPF ChaosDvdOverlay.StepOne/Begin/FadeOutAndRetire, on a stepped clock.</summary>
public class ChaosDvdFlockTests
{
    private sealed class SteppedTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (ChaosDvdFlock Flock, SteppedTime Time) NewFlock()
    {
        var t = new SteppedTime();
        var f = new ChaosDvdFlock(new Random(7), t) { Left = 0, Top = 0, Right = 1000, Bottom = 600 };
        f.Measure = (_, _) => (100, 50);
        return (f, t);
    }

    private static ChaosDvdLogo One(ChaosDvdFlock f, double x, double y, double vx, double vy, int splits = 0)
    {
        var l = f.Launch(60, 1, 1, splitBounces: splits).Single();
        (l.X, l.Y, l.Vx, l.Vy) = (x, y, vx, vy);
        return l;
    }

    [Fact]
    public void RightWallClampsReversesAndStepsTheHue()
    {
        var (f, _) = NewFlock();
        var l = One(f, 895, 300, 200, 0);
        int hue = l.HueIndex;
        var frame = f.Step(0.1);
        Assert.Equal(900, l.X);
        Assert.Equal(-200, l.Vx);
        Assert.Equal((hue + 1) % ChaosDvdFlock.Hues.Length, l.HueIndex);
        Assert.Contains(l, frame.Recolored);
        Assert.True(frame.BounceCue);
    }

    [Fact]
    public void TopAndLeftWallsBounceToo_FreeFlightDoesNot()
    {
        var (f, _) = NewFlock();
        var l = One(f, 5, 5, -100, -100);
        f.Step(0.1);
        Assert.Equal((0.0, 0.0, 100.0, 100.0), (l.X, l.Y, l.Vx, l.Vy));
        var free = f.Step(0.1);
        Assert.Equal((10.0, 10.0), (l.X, l.Y));
        Assert.Empty(free.Recolored);
    }

    [Fact]
    public void BounceCueIsThrottledTo250ms()
    {
        var (f, t) = NewFlock();
        var l = One(f, 895, 300, 200, 0);
        Assert.True(f.Step(0.1).BounceCue);
        l.X = 5; l.Vx = -200;
        t.Now += TimeSpan.FromMilliseconds(100);
        Assert.False(f.Step(0.1).BounceCue);
        l.X = 895; l.Vx = 200;
        t.Now += TimeSpan.FromMilliseconds(150);
        Assert.True(f.Step(0.1).BounceCue);
    }

    [Fact]
    public void CastingCouchSplitsOnAWallHitWithOneFewerSplit()
    {
        var (f, _) = NewFlock();
        var l = One(f, 895, 300, 200, 0, splits: 2);
        var frame = f.Step(0.1);
        var twin = Assert.Single(frame.Spawned);
        Assert.True(frame.LaunchCue);
        Assert.Equal(1, l.SplitBouncesLeft);
        Assert.Equal(1, twin.SplitBouncesLeft);
        Assert.Equal((l.X, l.Y), (twin.X, twin.Y));
        Assert.Equal(200, Math.Sqrt(twin.Vx * twin.Vx + twin.Vy * twin.Vy), 6);
        Assert.Equal(Math.Cos(0.61), (l.Vx * twin.Vx + l.Vy * twin.Vy) / (200.0 * 200.0), 6);   // ~35° off the parent
    }

    [Fact]
    public void ToyCapStopsSplitsAtEight_ThoughtCapAtEight()
    {
        var (f, _) = NewFlock();
        for (int i = 0; i < 7; i++) One(f, 500, 300, 0, 0);   // + the splitter = 8 alive
        var l = One(f, 895, 300, 200, 0, splits: 2);
        Assert.Empty(f.Step(0.1).Spawned);
        Assert.Equal(1, l.SplitBouncesLeft);

        var (g, _) = NewFlock();
        for (int i = 0; i < 6; i++) g.Launch(10, 1, 1, count: 2, text: "good girl");
        Assert.Equal(ChaosDvdFlock.MaxThoughts, g.Active.Count);
        Assert.False(g.AnyToyActive);
    }

    [Fact]
    public void LifetimeEndsInA240msFadeThenRemoval()
    {
        var (f, _) = NewFlock();
        var l = f.Launch(1, 1, 1).Single();
        f.Step(0.09);
        Assert.Equal(ChaosDvdFlock.PeakOpacity / 2, l.Opacity, 6);
        f.Step(0.1); f.Step(0.1); f.Step(0.1); f.Step(0.1); f.Step(0.1);
        f.Step(0.1); f.Step(0.1); f.Step(0.1); f.Step(0.1);   // 0.99 s
        Assert.True(l.FadeOutSec < 0);
        f.Step(0.02);
        Assert.Equal(0, l.FadeOutSec);
        f.Step(0.12);
        Assert.Equal(ChaosDvdFlock.PeakOpacity / 2, l.Opacity, 6);
        var end = f.Step(0.12);
        Assert.Contains(l, end.Removed);
        Assert.False(f.AnyActive);
    }

    [Fact]
    public void LaunchHeadingIs20To80DegreesAtTheRankSpeed()
    {
        var (f, _) = NewFlock();
        for (int i = 0; i < 50; i++)
        {
            var l = f.Launch(5, 5, 9).Single();   // speed clamps at 2x, scale at 1.5
            double ang = Math.Atan2(Math.Abs(l.Vy), Math.Abs(l.Vx)) * 180 / Math.PI;
            Assert.InRange(ang, 20, 80);
            Assert.Equal(ChaosDvdFlock.BaseSpeed * 2, Math.Sqrt(l.Vx * l.Vx + l.Vy * l.Vy), 6);
            Assert.Equal(1.5, l.FontScale);
            Assert.InRange(l.X, 0, 900);
            Assert.InRange(l.Y, 0, 550);
            f.Clear();
        }
    }
}
