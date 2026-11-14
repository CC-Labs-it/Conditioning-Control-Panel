using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessInputProbeTests
{
    [Fact]
    public void IdleIsSecondsAndUnavailableInputFailsOpen()
    {
        (long, int, int)? sample = (12500, 10, 10);
        using var probe = new X11AwarenessInputProbe(() => sample);
        Assert.Equal(12, probe.IdleSeconds);
        sample = null;
        Assert.Equal(0, probe.IdleSeconds);
        sample = (-1, 0, 0);
        Assert.Equal(0, probe.IdleSeconds);
    }

    [Fact]
    public void TypingBurstNeedsEightFreshStationarySamplesAndAgesOut()
    {
        long now = 10000;
        (long, int, int)? sample = (20, 10, 10);
        using var probe = new X11AwarenessInputProbe(() => sample, () => now);
        probe.Start();
        probe.SampleOnce(); // establish a baseline
        for (int i = 0; i < 7; i++) { now += 250; probe.SampleOnce(); }
        Assert.False(probe.IsTypingBurst);
        now += 250; probe.SampleOnce();
        Assert.True(probe.IsTypingBurst);
        // No fresh input for a whole window clears the burst.
        for (int i = 0; i < 16; i++) { now += 250; sample = (20 + 250L * (i + 1), 10, 10); probe.SampleOnce(); }
        Assert.False(probe.IsTypingBurst);
    }

    [Fact]
    public void VariableX11QueryLatencyDoesNotMimicInput()
    {
        long now = 100000, lastInput = now - 5000;
        int reads = 0;
        bool sampleAtEnd = false;
        using var probe = new X11AwarenessInputProbe(() =>
        {
            int delay = (reads++ % 4) switch { 0 => 0, 1 => 10, 2 => 65, _ => 200 };
            long idle = now + (sampleAtEnd ? delay : 0) - lastInput;
            now += delay;
            return (idle, 10, 10);
        }, () => now);
        probe.Start();
        for (int phase = 0; phase < 2; phase++)
        {
            sampleAtEnd = phase == 1;
            for (int i = 0; i < 32; i++) { now += 250; probe.SampleOnce(); }
            Assert.False(probe.IsTypingBurst);
        }
        // Real input still accumulates despite the same variable latency.
        for (int i = 0; i < 8; i++) { now += 250; lastInput = now; probe.SampleOnce(); }
        Assert.True(probe.IsTypingBurst);
    }

    [Fact]
    public void PointerMovementDoesNotCount()
    {
        long now = 10000; int x = 0;
        using var probe = new X11AwarenessInputProbe(() => (20, x, 0), () => now);
        probe.Start(); probe.SampleOnce();
        for (int i = 0; i < 16; i++) { now += 250; x++; probe.SampleOnce(); }
        Assert.False(probe.IsTypingBurst);
    }

    [Fact]
    public void StopClearsTheBurst()
    {
        long now = 10000;
        using var probe = new X11AwarenessInputProbe(() => (20, 0, 0), () => now);
        probe.Start(); probe.SampleOnce();
        for (int i = 0; i < 8; i++) { now += 250; probe.SampleOnce(); }
        Assert.True(probe.IsTypingBurst);
        probe.Stop();
        Assert.False(probe.IsTypingBurst);
        probe.SampleOnce();
        Assert.False(probe.IsTypingBurst);
    }

    [Fact]
    public void RestartKeepsTheLastInputTimestamp()
    {
        long now = 10000; long idle = 20;
        using var probe = new X11AwarenessInputProbe(() => (idle, 0, 0), () => now);
        probe.Start(); probe.SampleOnce();
        now += 250; probe.SampleOnce();
        probe.Stop();
        probe.Start();
        now += 250; idle = 270; probe.SampleOnce(); // no input since the last sample
        idle = 20;
        for (int i = 0; i < 7; i++) { now += 250; probe.SampleOnce(); }
        Assert.False(probe.IsTypingBurst);
        now += 250; probe.SampleOnce();
        Assert.True(probe.IsTypingBurst);
    }
}
