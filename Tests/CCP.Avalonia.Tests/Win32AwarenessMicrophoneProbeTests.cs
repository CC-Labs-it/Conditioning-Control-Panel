using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Win32AwarenessMicrophoneProbeTests
{
    [Fact]
    public void SweepResultIsCachedForFiveSeconds()
    {
        int calls = 0;
        var probe = new Win32AwarenessMicrophoneProbe(() => ++calls == 1);
        var now = new DateTime(2026, 1, 1);
        Assert.True(probe.IsInUse(now));
        Assert.True(probe.IsInUse(now.AddSeconds(4.999)));
        Assert.Equal(1, calls);
        Assert.False(probe.IsInUse(now.AddSeconds(5)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void NativeSweepIsSafeOnWindows()
    {
        if (OperatingSystem.IsWindows()) _ = new Win32AwarenessMicrophoneProbe().IsInUse(DateTime.Now);
    }
}
