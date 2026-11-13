using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessLinuxProbesTests
{
    [Theory]
    [InlineData("[]", false)]
    [InlineData("[{\"index\":1}]", true)]
    [InlineData("{}", true)]
    [InlineData("invalid", true)]
    [InlineData(null, true)]
    public void MicrophoneCaptureAndUnknownAreBusy(string? sourceOutputs, bool busy)
    {
        var probe = new AwarenessMicrophoneProbe(() => sourceOutputs);
        Assert.Equal(busy, probe.IsInUse(DateTime.UtcNow));
    }

    [Fact]
    public void MicrophoneCachesAndRecoversAfterFailure()
    {
        string? output = null;
        var probe = new AwarenessMicrophoneProbe(() => output);
        var now = DateTime.UtcNow;
        Assert.True(probe.IsInUse(now));
        output = "[]";
        Assert.True(probe.IsInUse(now.AddSeconds(4)));
        Assert.False(probe.IsInUse(now.AddSeconds(5)));
    }

    [Fact]
    public void MicrophoneReaderExceptionIsBusy()
    {
        var probe = new AwarenessMicrophoneProbe(() => throw new InvalidOperationException());
        Assert.True(probe.IsInUse(DateTime.UtcNow));
    }
}
