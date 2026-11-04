using System;
using System.Threading;
using System.Runtime.InteropServices;
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

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

    [Fact]
    public void WindowsCaptureMatchesAStableDesktopPatch()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Requires an interactive Windows desktop");
        var dc = GetDC(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, dc);
        int px = 0, py = 0; uint color = 0; bool found = false;
        try
        {
            for (int y = 0; y < 128 && !found; y += 4)
                for (int x = 0; x < 128 && !found; x += 4)
                {
                    var candidate = GetPixel(dc, x, y);
                    if (candidate == 0xffffffff || (candidate & 0xffffff) < 0x303030) continue;
                    bool uniform = true;
                    for (int dy = 0; dy < 4 && uniform; dy++)
                        for (int dx = 0; dx < 4; dx++)
                            if (GetPixel(dc, x + dx, y + dy) != candidate) { uniform = false; break; }
                    if (uniform) { px = x; py = y; color = candidate; found = true; }
                }
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        Assert.SkipWhen(!found, "No stable coloured desktop patch to sample");
        var pump = new BrainDrainCapturePump(new global::Avalonia.PixelRect(px, py, 4, 4), TimeSpan.FromMilliseconds(33));
        try
        {
            BrainDrainCapturePump.Frame? frame = null;
            Assert.True(SpinWait.SpinUntil(() => (frame = pump.Take()) != null, TimeSpan.FromSeconds(10)));
            var pixels = frame!.Pixels;
            Assert.InRange(Math.Abs(pixels[0] - (int)((color >> 16) & 255)), 0, 16);
            Assert.InRange(Math.Abs(pixels[1] - (int)((color >> 8) & 255)), 0, 16);
            Assert.InRange(Math.Abs(pixels[2] - (int)(color & 255)), 0, 16);
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
