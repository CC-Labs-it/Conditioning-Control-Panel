using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The WPF gif rain's maths (Chaos/ChaosGifCascadeOverlay.cs + GifCascadePayload) in Core GifCascade.</summary>
public class GifCascadeTests
{
    private static GifCascade Payload(out List<GifCascade.Faller> spawned, double height = 1000, int seed = 7)
    {
        var c = new GifCascade(new Random(seed));
        var list = new List<GifCascade.Faller>();
        c.Spawned += list.Add;
        c.Restart(5, GifCascade.PayloadSpawnRatePerSec, GifCascade.PayloadDurationSec, GifCascade.PayloadGifSize,
            GifCascade.PayloadFallSpeed, GifCascade.PayloadOpacity, GifCascade.PayloadStartScale, height, 0, 1920);
        spawned = list;
        return c;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedSpawnsReleaseResourcesAndStopOnTime(bool persistentFailures, bool cleanupFails)
    {
        var c = new GifCascade(new Random(1));
        var gone = new List<GifCascade.Faller>();
        var attempts = 0;
        c.Gone += f =>
        {
            gone.Add(f);
            if (cleanupFails) throw new InvalidOperationException("cleanup failed");
        };
        c.Spawned += f =>
        {
            attempts++;
            f.Animated = true;
            if (persistentFailures || attempts <= 2) throw new InvalidOperationException("spawn failed");
        };
        c.Restart(1, 1, 4, 100, 4, 1, 1, 100_000, 0, 1000);
        Assert.Empty(c.Fallers);
        Assert.Equal(0, c.AnimatedAlive);
        Assert.Single(gone);

        c.Step(1); // A failure during Step must still advance the next spawn time.
        Assert.Equal(2, attempts);
        Assert.Empty(c.Fallers);
        c.Step(0.25);
        Assert.Equal(2, attempts);
        c.Step(1.75); // Subsequent spawns can succeed, or fail without leaking slots.
        Assert.Equal(4, attempts);
        Assert.Equal(persistentFailures ? 0 : 2, c.AnimatedAlive);
        Assert.Equal(persistentFailures ? 4 : 2, gone.Count);
        Assert.Equal(gone.Count, gone.Distinct().Count());
        Assert.All(gone, f => Assert.False(f.Animated));

        c.Step(1); // End of the four-second spawn window, even if every spawn failed.
        Assert.False(c.Spawning);
        c.Clear();
        c.Step(10);
        Assert.Equal(4, attempts);
        Assert.Empty(c.Fallers);
        Assert.False(c.IsRaining);
    }

    [Fact]
    public void FailedSpawnsDuringOneLongFrameStillEndAtTheDeadline()
    {
        var c = new GifCascade(new Random(1));
        var attempts = 0;
        c.Spawned += f =>
        {
            f.Animated = true;
            if (++attempts > 1) throw new InvalidOperationException("spawn failed");
        };
        c.Restart(1, 1, 4, 100, 4, 1, 1, 100_000, 0, 1000);
        c.Step(4);
        Assert.Equal(4, attempts);
        Assert.Single(c.Fallers);
        Assert.Equal(1, c.AnimatedAlive);
        Assert.False(c.Spawning);
    }

    [Theory]
    [InlineData(1.67, 6.0, 17)]   // the payload: ceil(10.02) + 6
    [InlineData(0.1, 0.5, 8)]     // floor 8 (duration counts as at least 1 s)
    [InlineData(10, 10, 24)]      // ceiling 24
    public void BatchSizeMatchesWpf(double rate, double dur, int expected) =>
        Assert.Equal(expected, GifCascade.BatchSize(rate, dur));

    [Fact]
    public void ThePayloadSpawnsElevenClipsOverSixSecondsThenStops()
    {
        var c = Payload(out var spawned, height: 100_000);   // nothing lands, so the cap never bites
        Assert.Single(spawned);                              // one at once (WPF Restart -> SpawnOne)
        Assert.True(c.Spawning);
        for (int i = 0; i < 59; i++) c.Step(0.01);           // 0.59 s: before the first 598.8 ms tick
        Assert.Single(spawned);
        c.Step(0.01);
        Assert.Equal(2, spawned.Count);
        for (int i = 0; i < 545; i++) c.Step(0.01);          // past 6.0 s
        Assert.Equal(11, spawned.Count);                     // 1 + ticks at k * 0.5988 s, k = 1..10
        Assert.False(c.Spawning);
        for (int i = 0; i < 200; i++) c.Step(0.01);
        Assert.Equal(11, spawned.Count);
        Assert.True(c.IsRaining);                            // still falling
        Assert.True(spawned.Select(f => f.Speed).Distinct().Count() > 5);   // each clip 0.7-1.3x the fall speed
    }

    [Fact]
    public void ALongFrameNeverSpawnsPastTheWindow()
    {
        var c = Payload(out var spawned, height: 100_000);
        c.Step(5.5);
        Assert.Equal(10, spawned.Count);                     // ticks to 5.389 s caught up
        c.Step(1.5);                                         // 7.0 s: 5.988 spawns, 6.587 does not
        Assert.Equal(11, spawned.Count);
    }

    [Fact]
    public void ClipsFallAtTheirSpeedPerSixteenMsWithTheDeltaCapped()
    {
        var c = Payload(out var spawned);
        var f = spawned[0];
        Assert.Equal(-400, f.Y);
        Assert.InRange(f.Speed, 3.6 * 0.7, 3.6 * 1.3);
        c.Step(0.016);
        Assert.Equal(-400 + f.Speed, f.Y, 6);
        c.Step(5.0);                                          // a stalled frame moves at most 100 ms worth
        Assert.Equal(-400 + f.Speed + f.Speed * 0.1 / 0.016, f.Y, 6);
    }

    [Fact]
    public void ClipsDespawnBelowTheBottomAndTheRainDrains()
    {
        var c = Payload(out var spawned, height: 600);
        var gone = new List<GifCascade.Faller>();
        c.Gone += gone.Add;
        var first = spawned[0];
        while (first.Y <= 600 + 400) { Assert.DoesNotContain(first, gone); c.Step(0.016); }
        Assert.Contains(first, gone);
        Assert.DoesNotContain(first, c.Fallers);
        for (int i = 0; i < 2000 && c.IsRaining; i++) c.Step(0.016);
        Assert.False(c.IsRaining);
        Assert.Equal(spawned.Count, gone.Count);
    }

    [Fact]
    public void ClipsGrowFromStartScaleToFullByThreeQuartersDown()
    {
        var c = Payload(out _, height: 1000);
        Assert.Equal(0.45, c.ScaleAt(-400), 6);
        Assert.Equal(0.45, c.ScaleAt(0), 6);
        Assert.Equal(0.725, c.ScaleAt(375), 6);
        Assert.Equal(1.0, c.ScaleAt(750), 6);
        Assert.Equal(1.0, c.ScaleAt(990), 6);
        c.Restart(1, 1, 1, 200, 4, 1, 1.0, 1000, 0, 100);
        Assert.Equal(1.0, c.ScaleAt(0), 6);
    }

    [Fact]
    public void NeverMoreThanFourteenClipsAlive()
    {
        var c = new GifCascade(new Random(1));
        int peak = 0;
        c.Spawned += _ => peak = Math.Max(peak, c.Fallers.Count);
        c.Restart(3, 20, 10, 200, 0.5, 1, 1, 100_000, 0, 1920);
        for (int i = 0; i < 1000; i++) c.Step(0.01);
        Assert.Equal(GifCascade.MaxConcurrent, peak);
        Assert.Equal(14, c.Fallers.Count);
    }

    [Fact]
    public void KnobsClampAndClipsSpawnInsideTheBand()
    {
        var c = new GifCascade(new Random(3));
        c.Restart(4, 100, 0.1, 5000, 99, 2, 0.01, 1000, 1920, 1920);
        Assert.Equal(600, c.Size);
        Assert.Equal(30, c.FallSpeed);
        Assert.Equal(1.0, c.Opacity);
        Assert.Equal(0.1, c.StartScale);
        for (int i = 0; i < 200; i++) c.Step(0.005);
        Assert.All(c.Fallers, f => Assert.InRange(f.CenterX, 1920 + 300, 1920 + 300 + 1320));
        Assert.All(c.Fallers, f => Assert.InRange(f.FileIndex, 0, 3));
    }

    [Fact]
    public void AnEmptyPoolRainsNothing()
    {
        var c = new GifCascade();
        c.Restart(0, 1.67, 6, 400, 3.6, 0.9, 0.45, 1000, 0, 1920);
        Assert.False(c.IsRaining);
    }

    [Fact]
    public void OnlyThreeSmallAnimatedClipsAnimate()
    {
        var c = Payload(out var spawned, height: 100_000);
        Assert.True(c.CanAnimate(true, 3_000_000));
        Assert.False(c.CanAnimate(true, 3_000_001));
        Assert.False(c.CanAnimate(true, 0));
        Assert.False(c.CanAnimate(false, 1000));
        for (int i = 0; i < 3; i++) { c.Step(0.6); spawned[^1].Animated = true; }
        Assert.Equal(3, c.AnimatedAlive);
        Assert.False(c.CanAnimate(true, 1000));
    }

    [Fact]
    public void ClearDropsEveryClip()
    {
        var c = Payload(out var spawned, height: 100_000);
        var gone = new List<GifCascade.Faller>();
        c.Gone += gone.Add;
        c.Step(1.3);
        c.Clear();
        Assert.False(c.IsRaining);
        Assert.Equal(spawned.Count, gone.Count);
    }

    [Fact]
    public void SpawnBandIsTheUnionOfTheResolvedScreensClippedToTheWindow()
    {
        Assert.Equal((0.0, 3840.0), GifCascade.SpawnBand(Array.Empty<(double, double)>(), 0, 3840));
        Assert.Equal((1920.0, 1920.0), GifCascade.SpawnBand(new[] { (1920.0, 3840.0) }, 0, 3840));
        Assert.Equal((0.0, 3840.0), GifCascade.SpawnBand(new[] { (1920.0, 3840.0), (0.0, 1920.0) }, 0, 3840));
        Assert.Equal((1000.0, 2000.0), GifCascade.SpawnBand(new[] { (0.0, 2000.0) }, -1000, 3000));   // window-local
        Assert.Equal((2000.0, 1000.0), GifCascade.SpawnBand(new[] { (2000.0, 4000.0) }, 0, 3000));      // clipped
        Assert.Equal((0.0, 500.0), GifCascade.SpawnBand(new[] { (10.0, 10.0) }, 0, 500));              // degenerate
    }
}
