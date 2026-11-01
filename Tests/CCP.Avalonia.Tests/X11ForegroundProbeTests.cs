using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class X11ForegroundProbeTests
{
    [Fact]
    public void SandboxNeverReadsTheDesktop()
    {
        Assert.Null(new X11ForegroundProbe().Read());
        Assert.Equal("", X11ActiveWindow.ReadTitle());
    }

    [Theory]
    [InlineData(2, 2, 1916u, 1076u, true)]
    [InlineData(3, 0, 1917u, 1080u, false)]
    [InlineData(0, 3, 1920u, 1077u, false)]
    [InlineData(0, 0, 1917u, 1080u, false)]
    [InlineData(0, 0, 1920u, 1077u, false)]
    public void FullscreenRequiresAllEdgesWithinTwoPixels(int x, int y, uint width, uint height, bool expected) =>
        Assert.Equal(expected, X11ActiveWindow.CoversMonitor(x, y, width, height, 0, 0, 1920, 1080));

    [Fact]
    public void FullscreenUsesMonitorCoordinatesAndRejectsInvalidMonitors()
    {
        Assert.True(X11ActiveWindow.CoversMonitor(-1922, -2, 1922, 1084, -1920, 0, 1920, 1080));
        Assert.False(X11ActiveWindow.CoversMonitor(0, 0, 1920, 1080, 0, 0, 0, 1080));
    }

    [Fact]
    public void SpanningWindowUsesTheMonitorWithTheLargestOverlap()
    {
        var monitors = new[] { (0, 0, 800, 600), (800, 0, 1920, 1080) };
        Assert.False(X11ActiveWindow.CoversBestMonitor(0, 0, 2000, 1080, monitors));
        Assert.True(X11ActiveWindow.CoversBestMonitor(0, 0, 800, 600, monitors));
        Assert.False(X11ActiveWindow.CoversBestMonitor(700, 0, 600, 600, monitors));
        Assert.True(X11ActiveWindow.CoversBestMonitor(700, 0, 600, 600, monitors, new uint[] { 700, 0, 0, 0 }));
    }

    [Fact]
    public void DecorationsAreIncludedBeforeMonitorSelectionAndCoverage()
    {
        var monitors = new[] { (0, 0, 1920, 1080) };
        Assert.False(X11ActiveWindow.CoversBestMonitor(8, 30, 1904, 1042, monitors));
        Assert.True(X11ActiveWindow.CoversBestMonitor(8, 30, 1904, 1042, monitors, new uint[] { 8, 8, 30, 8 }));
        Assert.False(X11ActiveWindow.CoversBestMonitor(8, 30, 1904, 1042, monitors, new uint[] { 8, 8, 30 }));
    }

    [Fact]
    public void SampleCarriesIdentityForPrivacy()
    {
        var window = new IntPtr(42);
        var sample = X11ActiveWindow.CreateSample(window, "Private Window", "FireFox.EXE", false, true);
        Assert.Equal(window, sample.Handle);
        Assert.Equal("Private Window", sample.Title);
        Assert.Equal("firefox", sample.ProcessName);
        Assert.True(sample.IsFullscreen);
        Assert.True(AwarenessObserverPolicy.IsIncognitoTitle(sample.Title));
        Assert.Equal("", X11ActiveWindow.CreateSample(window, "", null, false, false).ProcessName);
    }

    [Fact]
    public void DesktopIsNeverFullscreen()
    {
        Assert.False(X11ActiveWindow.CreateSample(new IntPtr(42), "", null, true, true).IsFullscreen);
        Assert.False(X11ActiveWindow.CreateSample(new IntPtr(42), "", null, false, false).IsFullscreen);
    }
}
