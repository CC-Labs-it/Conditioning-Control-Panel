using System;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainCapturePumpTests
{
    [Fact]
    public void DesktopCopyExcludesLayeredWindows()
    {
        Assert.Equal(0x00CC0020u, BrainDrainCapturePump.DesktopCopy);
        Assert.Equal(0u, BrainDrainCapturePump.DesktopCopy & 0x40000000u);
    }

    [Fact]
    public void WindowsDesktopCaptureProducesOpaquePixels()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Requires an interactive Windows desktop");
        var pump = new BrainDrainCapturePump(new global::Avalonia.PixelRect(0, 0, 64, 64), TimeSpan.FromMilliseconds(33));
        try
        {
            BrainDrainCapturePump.Frame? frame = null;
            Assert.True(SpinWait.SpinUntil(() => (frame = pump.Take()) != null, TimeSpan.FromSeconds(10)));
            Assert.Equal(16, frame!.Width);
            Assert.Equal(16, frame.Height);
            Assert.Equal(16 * 16 * 4, frame.Pixels.Length);
            for (var i = 3; i < frame.Pixels.Length; i += 4) Assert.Equal(255, frame.Pixels[i]);
        }
        finally { pump.Shutdown(); }
    }

    [Fact]
    public void BlockedCaptureDoesNotBlockShutdownOrPublishAfterwards()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var pump = new BrainDrainCapturePump(() => {
            entered.Set(); release.Wait(); finished.Set();
            return new BrainDrainCapturePump.Frame(1, 1, new byte[4]);
        }, TimeSpan.FromMilliseconds(10));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var stop = System.Threading.Tasks.Task.Run(pump.Shutdown);
            Assert.True(stop.Wait(TimeSpan.FromSeconds(1)));
            Assert.Null(pump.Take());
        }
        finally { release.Set(); }
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        // Let the worker attempt the handoff after returning from the blocked operation.
        Assert.True(SpinWait.SpinUntil(() => pump.Take() != null, TimeSpan.FromMilliseconds(200)) == false);
    }

    [Fact]
    public void CompletedFrameCanBeTakenOnlyOnceAndShutdownClearsIt()
    {
        using var entered = new ManualResetEventSlim();
        var pump = new BrainDrainCapturePump(() => { entered.Set(); return new BrainDrainCapturePump.Frame(1, 1, new byte[4]); }, TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            BrainDrainCapturePump.Frame? frame = null;
            Assert.True(SpinWait.SpinUntil(() => (frame = pump.Take()) != null, TimeSpan.FromSeconds(5)));
            Assert.NotNull(frame);
            Assert.Null(pump.Take());
        }
        finally { pump.Shutdown(); }
        Assert.Null(pump.Take());
    }
}
