using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Win32AwarenessInputProbeTests
{
    [Fact]
    public void IdleUsesUnsignedTickWrapAndHandlesFailedReads()
    {
        bool success = true;
        using var probe = new Win32AwarenessInputProbe(() => (success, uint.MaxValue - 999), () => (true, 0, 0), () => 1000, TimeSpan.FromDays(1));
        Assert.Equal(2, probe.IdleSeconds);
        success = false;
        Assert.Equal(0, probe.IdleSeconds);
    }

    [Fact]
    public void BurstThresholdStopAndRestart()
    {
        uint tick = 0;
        using var probe = new Win32AwarenessInputProbe(() => (true, ++tick), () => (true, 0, 0), () => tick, TimeSpan.FromDays(1));
        probe.Start();
        for (int i = 0; i < 7; i++) probe.Sample();
        Assert.False(probe.IsTypingBurst);
        probe.Sample();
        Assert.True(probe.IsTypingBurst);
        probe.Stop();
        Assert.False(probe.IsTypingBurst);
        for (int i = 0; i < 8; i++) probe.Sample();
        Assert.False(probe.IsTypingBurst);
        probe.Start();
        for (int i = 0; i < 8; i++) probe.Sample();
        Assert.True(probe.IsTypingBurst);
    }

    [Fact]
    public void CursorMovementIsNotTypingAndOldSamplesExpire()
    {
        uint tick = 0;
        int x = 0;
        using var probe = new Win32AwarenessInputProbe(() => (true, ++tick), () => (true, x, 0), () => tick, TimeSpan.FromDays(1));
        probe.Start();
        for (int i = 0; i < 8; i++) { x++; probe.Sample(); }
        Assert.False(probe.IsTypingBurst);
        for (int i = 0; i < 8; i++) probe.Sample();
        Assert.True(probe.IsTypingBurst);
        for (int i = 0; i < 9; i++) { x++; probe.Sample(); }
        Assert.False(probe.IsTypingBurst);
    }

    [Fact]
    public void FailedNativeReadsDoNotCrashOrInventInput()
    {
        bool success = false;
        bool throwRead = false;
        uint tick = 0;
        using var probe = new Win32AwarenessInputProbe(() => throwRead ? throw new InvalidOperationException() : (success, ++tick), () => (false, 0, 0), () => tick, TimeSpan.FromDays(1));
        probe.Start();
        for (int i = 0; i < 9; i++) probe.Sample();
        Assert.False(probe.IsTypingBurst);
        throwRead = true;
        probe.Sample();
        Assert.Equal(0, probe.IdleSeconds);
        throwRead = false;
        success = true;
        for (int i = 0; i < 8; i++) probe.Sample();
        Assert.True(probe.IsTypingBurst);
    }
}
