using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Long-stare detection on synthetic gaze points with a fixed clock: no camera, no frames.</summary>
public class LongStareDetectorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // 30 points 110 ms apart = 3.19 s, the full buffer.
    private static (double X, double Y)? Feed(LongStareDetector d, DateTime start, Func<int, (double, double)> at)
    {
        (double X, double Y)? hit = null;
        for (int i = 0; i < 30; i++)
        {
            var (x, y) = at(i);
            hit = d.Push(start.AddMilliseconds(i * 110), x, y) ?? hit;
        }
        return hit;
    }

    [Fact]
    public void FiresAtTheCentroid_AfterThreeSecondsStill_ThenCoolsDown()
    {
        var d = new LongStareDetector();
        var hit = Feed(d, T0, i => (500 + (i % 2 == 0 ? 10 : -10), 300));
        Assert.NotNull(hit);
        Assert.InRange(hit!.Value.X, 499.0, 501.0);
        Assert.Equal(300.0, hit.Value.Y, 6);

        // Still staring 2 s later: inside the 5 s cooldown, nothing.
        Assert.Null(d.Push(T0.AddMilliseconds(29 * 110 + 2000), 500, 300));
        // Once the cooldown has passed the still-steady buffer fires again.
        Assert.NotNull(d.Push(T0.AddMilliseconds(29 * 110 + 5000), 500, 300));
    }

    [Fact]
    public void NeedsTheFullDuration_AndEveryPointInsideTheRadius()
    {
        var shortRun = new LongStareDetector();
        for (int i = 0; i < 20; i++)
            Assert.Null(shortRun.Push(T0.AddMilliseconds(i * 100), 500, 300)); // 1.9 s

        var wandering = new LongStareDetector();
        Assert.Null(Feed(wandering, T0, i => (i == 15 ? 700 : 500, 300))); // one point 190 px off
    }

    [Fact]
    public void ClearDropsTheBuffer()
    {
        var d = new LongStareDetector();
        // 28 points end at 2.97 s: just short of a stare, so no cooldown is armed yet.
        for (int i = 0; i < 28; i++) Assert.Null(d.Push(T0.AddMilliseconds(i * 110), 500, 300));
        d.Clear();
        // Without the clear this point (3.08 s after the first) would complete a stare.
        Assert.Null(d.Push(T0.AddMilliseconds(28 * 110), 500, 300));
    }
}
