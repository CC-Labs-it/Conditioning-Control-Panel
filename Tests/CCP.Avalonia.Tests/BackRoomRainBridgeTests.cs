using System;
using ConditioningControlPanel.Avalonia.Views.BackRoom;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BackRoomRainBridgeTests
{
    [Theory]
    [InlineData(9, 3000, 3, 3)]
    [InlineData(0, 500, 1, 1)]
    public void RainUsesAuthoredParametersAndMinimums(int count, int ms, double rate, double duration)
    {
        double[]? args = null;
        var bridge = new BackRoomRainBridge((a, b, c, d, e, f) => args = [a, b, c, d, e, f],
            () => { }, () => false, () => false, () => 100);
        bridge.GifRain(count, ms, 0.37);
        Assert.Equal(new[] { rate, duration, GifCascadePayload.GIF_SIZE, GifCascadePayload.FALL_SPEED, 0.37, GifCascadePayload.START_SCALE }, args);
    }

    [Fact]
    public void ExistingRainIsLeftAloneAndNotOwned()
    {
        int shown = 0, closed = 0;
        var bridge = new BackRoomRainBridge((a, b, c, d, e, f) => shown++, () => closed++, () => true, () => false, () => 100);
        bridge.GifRain(3, 1000, 0.5);
        bridge.StopAll();
        Assert.Equal(0, shown);
        Assert.Equal(0, closed);
    }

    [Theory]
    [InlineData(11999, false, 1)]
    [InlineData(12000, false, 0)]
    [InlineData(1, true, 0)]
    public void StopOnlyClosesRecentRainOutsideChaosAndResetsOwnership(int elapsed, bool chaos, int expected)
    {
        long now = 100;
        int closed = 0;
        var bridge = new BackRoomRainBridge((a, b, c, d, e, f) => { }, () => closed++, () => false, () => chaos, () => now);
        bridge.GifRain(1, 1000, 1);
        now += elapsed;
        bridge.StopAll();
        Assert.Equal(expected, closed);
        bridge.StopAll();
        Assert.Equal(expected, closed);
    }
}
