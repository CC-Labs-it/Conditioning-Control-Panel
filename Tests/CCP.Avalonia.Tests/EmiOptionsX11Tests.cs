using System.Collections.Generic;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiOptionsX11Tests
{
    [Fact]
    public void WindowsPanelNeverActivatesButStillReceivesClicks()
    {
        const uint existing = 0x100;
        uint style = Win32OptionsDismiss.PassiveStyle(existing);
        Assert.Equal(existing | Win32Overlay.WsExToolWindow | Win32Overlay.WsExNoActivate, style);
        Assert.Equal(0u, style & Win32Overlay.WsExTransparent);
        Assert.Equal(style, Win32OptionsDismiss.PassiveStyle(style));
    }

    [Fact]
    public void BriefLeftAndRightPressesSurviveWithoutPollingHeldState()
    {
        var clicks = new List<PixelPoint?>();
        using var subscription = new X11PanicKey.OptionsSubscription((point, escape) =>
        {
            Assert.False(escape);
            clicks.Add(point);
        });
        // XI2 queues the press even when the release has already happened by dispatch time.
        X11PanicKey.RouteOptions(15, 1, 9, () => new PixelPoint(-42, 72));
        X11PanicKey.RouteOptions(16, 1, 9, () => null);
        X11PanicKey.RouteOptions(15, 3, 9, () => new PixelPoint(15, 20));
        Assert.Equal(new PixelPoint?[] { new(-42, 72), new(15, 20) }, clicks);
    }

    [Fact]
    public void OnlyEscapeDownOrLeftAndRightDownReachThePanel()
    {
        int calls = 0, reads = 0;
        using var subscription = new X11PanicKey.OptionsSubscription((point, escape) =>
        {
            Assert.True(escape);
            Assert.Null(point);
            calls++;
        });
        PixelPoint? Pointer() { reads++; return null; }
        X11PanicKey.RouteOptions(13, 9, 9, Pointer);
        X11PanicKey.RouteOptions(14, 9, 9, Pointer);
        X11PanicKey.RouteOptions(13, 38, 9, Pointer);
        X11PanicKey.RouteOptions(13, 0, 0, Pointer);
        X11PanicKey.RouteOptions(15, 2, 9, Pointer);
        X11PanicKey.RouteOptions(15, 4, 9, Pointer);
        X11PanicKey.RouteOptions(15, 1, 9, Pointer); // unavailable pointer is ignored
        Assert.Equal(1, calls);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void FoldUnsubscribesIdempotentlyWithoutRemovingAnotherPanel()
    {
        int first = 0, second = 0;
        var a = new X11PanicKey.OptionsSubscription((_, _) => first++);
        using var b = new X11PanicKey.OptionsSubscription((_, _) => second++);
        try
        {
            X11PanicKey.RouteOptions(13, 9, 9, () => null);
            a.Dispose();
            a.Dispose();
            X11PanicKey.RouteOptions(13, 9, 9, () => null);
            Assert.Equal(1, first);
            Assert.Equal(2, second);
        }
        finally { a.Dispose(); }
    }
}
