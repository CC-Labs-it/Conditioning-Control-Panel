using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Gaze lock-on on synthetic gaze points: no camera, no frames.</summary>
public class GazeLockTests
{
    [Fact]
    public void NoTarget_PassesThePointThrough()
    {
        var lk = new GazeLock();
        Assert.Equal((300.0, 200.0), lk.Apply(300, 200));
    }

    [Fact]
    public void HeldNearTheTarget_EngagementBuilds_AndContractsTowardIt()
    {
        var lk = new GazeLock();
        lk.SetTarget(100, 100, 50);

        // First frame: engagement 0.22 -> k = 0.93 * 0.22 inside the radius.
        var first = lk.Apply(120, 100);
        Assert.Equal(120 - 20 * 0.93 * 0.22, first.X, 6);
        Assert.Equal(100.0, first.Y, 6);

        (double X, double Y) p = first;
        for (int i = 0; i < 60; i++) p = lk.Apply(120, 100);
        // Full engagement: 93% of the deviation is gone.
        Assert.Equal(120 - 20 * 0.93, p.X, 3);
    }

    [Fact]
    public void BeyondFourRadii_ThePointIsUntouched()
    {
        var lk = new GazeLock();
        lk.SetTarget(0, 0, 10);
        for (int i = 0; i < 30; i++) lk.Apply(30, 0); // engage inside the 3.5x capture zone
        Assert.Equal((41.0, 0.0), lk.Apply(41, 0)); // engaged, but the taper is 0 past 4x
    }

    [Fact]
    public void ANewTarget_StartsFromZeroEngagement_ADriftingOneKeepsIt()
    {
        var lk = new GazeLock();
        lk.SetTarget(100, 100, 50);
        for (int i = 0; i < 60; i++) lk.Apply(120, 100);

        lk.SetTarget(110, 100, 50); // drifted by less than the radius: still fully engaged
        Assert.Equal(120 - 10 * 0.93, lk.Apply(120, 100).X, 3);

        lk.SetTarget(200, 100, 50); // jumped by more than the radius: one build step, 0.8 taper at 80 px
        Assert.Equal(120 + 80 * 0.93 * 0.22 * 0.8, lk.Apply(120, 100).X, 6);
    }

    [Fact]
    public void Clear_ReleasesImmediately()
    {
        var lk = new GazeLock();
        lk.SetTarget(100, 100, 50);
        for (int i = 0; i < 30; i++) lk.Apply(120, 100);
        lk.Clear();
        Assert.Equal((120.0, 100.0), lk.Apply(120, 100));
    }
}
