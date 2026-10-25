using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TongueOutDetectorTests
{
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 255, 39, 0)]
    [InlineData(0, 30, 200, 1)]
    [InlineData(20, 30, 40, 2)]
    [InlineData(160, 30, 40, 2)]
    [InlineData(179, 255, 255, 2)]
    [InlineData(21, 255, 255, 3)]
    [InlineData(159, 255, 255, 3)]
    [InlineData(0, 29, 100, 3)]
    public void HsvBandsAndClassificationOrder(byte h, byte s, byte v, int expected)
        => Assert.Equal(expected, TongueOutDetector.Classify(h, s, v));

    [Fact]
    public void HoldHysteresisCooldownAndSingleFirePerProtrusion()
    {
        var d = new TongueOutDetector();
        bool At(int ms, double ratio) => d.Update(true, ratio, Epoch.AddMilliseconds(ms));
        Assert.False(At(0, .28)); // strict enter
        Assert.False(At(1, .29));
        Assert.False(At(350, .20)); // retained above leave, but only 349 ms
        Assert.True(At(351, .20));
        Assert.False(At(1500, .99)); // never repeats while held
        Assert.False(At(1501, .18)); // strict leave
        Assert.False(At(1502, .29));
        Assert.True(At(1852, .29));
        Assert.False(At(1853, 0));
        Assert.False(At(1854, .29));
        Assert.False(At(2204, .29)); // hold met, cooldown not met
        Assert.True(At(2552, .29));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedMouthOrFaceLossCancelsPendingHold(bool faceLost)
    {
        var d = new TongueOutDetector();
        d.Update(true, .9, Epoch);
        if (faceLost) d.FaceMissing();
        else Assert.False(d.Update(false, .9, Epoch.AddMilliseconds(200)));
        Assert.False(d.Update(true, .9, Epoch.AddSeconds(1)));
        Assert.True(d.Update(true, .9, Epoch.AddMilliseconds(1350)));
        d.Reset();
        Assert.False(d.Update(true, .9, Epoch.AddMilliseconds(1400)));
        Assert.True(d.Update(true, .9, Epoch.AddMilliseconds(1750)));
    }
}
