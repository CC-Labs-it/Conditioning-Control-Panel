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

    private const string Sources = """
        [{"index":1,"name":"alsa_input.usb"},{"index":2,"name":"alsa_output.monitor"}]
        """;

    [Fact]
    public void MicrophoneRequiresAnUncorkedCaptureOnARealSource()
    {
        Assert.True(PulseAwarenessMicrophoneProbe.HasActiveCapture(Sources, """[{"source":1,"corked":false}]"""));
        Assert.True(PulseAwarenessMicrophoneProbe.HasActiveCapture(Sources, """[{"source":1,"corked":"no"}]"""));
        Assert.False(PulseAwarenessMicrophoneProbe.HasActiveCapture(Sources, """[{"source":2,"corked":false}]"""));
        Assert.False(PulseAwarenessMicrophoneProbe.HasActiveCapture(Sources, """[{"source":1,"corked":true}]"""));
        Assert.False(PulseAwarenessMicrophoneProbe.HasActiveCapture(Sources, """[{"source":1}]"""));
    }

    [Fact]
    public void MicrophoneCachesFiveSecondsAndFailureReadsAsNotInUse()
    {
        int reads = 0;
        bool live = true;
        var probe = new PulseAwarenessMicrophoneProbe(args =>
        {
            reads++;
            if (!live) throw new InvalidOperationException();
            return args.EndsWith("sources", StringComparison.Ordinal) ? Sources : """[{"source":1,"corked":false}]""";
        });
        var now = new DateTime(2026, 1, 1);
        Assert.True(probe.IsInUse(now));
        live = false;
        Assert.True(probe.IsInUse(now.AddSeconds(4)));
        Assert.Equal(2, reads);
        Assert.False(probe.IsInUse(now.AddSeconds(5)));
        Assert.Equal(3, reads);
    }
}
