using System;
using System.Drawing;
using ConditioningControlPanel.Services.ScreenCapture;
using Xunit;

namespace CCP.Core.Tests;

public class ScreenFramesTests
{
    private static ScreenFrame Frame(string screen, Rectangle bounds, double scale, byte[] pixels) =>
        new(screen, bounds, scale, 2, 1, 8, ScreenPixelFormat.Bgra32, pixels);

    [Fact]
    public void ChangesAreTrackedIndependentlyAndReset()
    {
        var changes = new ScreenFrameChanges();
        var a = Frame("a", new Rectangle(-2, 0, 2, 1), 1, new byte[8]);
        var b = Frame("b", new Rectangle(0, 0, 2, 1), 2, new byte[8]);
        Assert.True(changes.Changed(a));
        Assert.True(changes.Changed(b));
        Assert.False(changes.Changed(a));
        Assert.False(changes.Changed(b));
        var pixels = new byte[8]; pixels[7] = 1;
        Assert.True(changes.Changed(a with { Pixels = pixels }));
        Assert.False(changes.Changed(b));
        changes.Reset();
        Assert.True(changes.Changed(b));
    }

    [Fact]
    public void MapsFramePixelsToTwoPhysicalMonitorsWithoutApplyingScaleTwice()
    {
        var left = Frame("left", new Rectangle(-1920, 0, 1920, 1080), 1, new byte[8]);
        var right = Frame("right", new Rectangle(0, -100, 3840, 2160), 2, new byte[8]);
        Assert.Equal(new Rectangle(-960, 0, 960, 1080), left.ToScreen(new Rectangle(1, 0, 1, 1)));
        Assert.Equal(new Rectangle(1920, -100, 1920, 2160), right.ToScreen(new Rectangle(1, 0, 1, 1)));
    }

    [Fact]
    public void InvalidBufferIsRejected() => Assert.Throws<ArgumentException>(() =>
        new ScreenFrameChanges().Changed(Frame("a", new Rectangle(0, 0, 2, 1), 1, new byte[7])));
}
