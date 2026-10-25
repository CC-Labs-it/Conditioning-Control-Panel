using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashClipHostTests
{
    [Theory]
    [InlineData(1920, 1080, 480, 270)]
    [InlineData(100, 200, 100, 200)]
    [InlineData(0, 0, 2, 2)]
    public void DecodeIsBounded(int w, int h, int expectedW, int expectedH)
        => Assert.Equal((expectedW, expectedH), FlashClipPlayer.DecodeSize(w, h));

    [Theory]
    [InlineData(.1, .25)]
    [InlineData(1, 1)]
    [InlineData(8, 4)]
    public void PlaybackRateMatchesWpf(double input, float expected)
        => Assert.Equal(expected, FlashClipPlayer.Rate(input));

    [Fact]
    public void PanicInvalidatesPendingFlashBurst()
    {
        var generation = FlashOverlay.Generation;
        PanicSurfaces.All.Single(s => s.Id == "flash-clips").Stop(null);
        Assert.Equal(generation + 1, FlashOverlay.Generation);
    }

    [Fact]
    public void MissingClipNeverConsumesAdmission()
    {
        var before = FlashClipPlayer.Budget.Live;
        Assert.Null(FlashClipPlayer.TryStart(null, "missing.mp4", 100, 100, _ => { }, 1, out var capped));
        Assert.False(capped);
        Assert.Equal(before, FlashClipPlayer.Budget.Live);
    }
}
