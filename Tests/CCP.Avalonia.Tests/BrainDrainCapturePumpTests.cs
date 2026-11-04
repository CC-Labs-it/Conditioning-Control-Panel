using System;
using System.Buffers;
using System.Collections.Concurrent;
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
            using var owned = frame;
            Assert.Equal(16, frame!.Width);
            Assert.Equal(16, frame.Height);
            int length = checked(frame.Width * frame.Height * 4);
            Assert.Equal(16 * 16 * 4, length);
            Assert.True(frame.Pixels.Length >= length);
            for (var i = 3; i < length; i += 4) Assert.Equal(255, frame.Pixels[i]);
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
            using var owned = frame;
            var pixels = frame!.Pixels;
            Assert.InRange(Math.Abs(pixels[0] - (int)((color >> 16) & 255)), 0, 16);
            Assert.InRange(Math.Abs(pixels[1] - (int)((color >> 8) & 255)), 0, 16);
            Assert.InRange(Math.Abs(pixels[2] - (int)(color & 255)), 0, 16);
        }
        finally { pump.Shutdown(); }
    }

    internal sealed class TrackingPool : ArrayPool<byte>
    {
        private readonly ConcurrentQueue<byte[]> _available = new();
        private int _returns;
        public int Returns => Volatile.Read(ref _returns);
        public override byte[] Rent(int length) => _available.TryDequeue(out var bytes) ? bytes : new byte[length];
        public override void Return(byte[] bytes, bool clearArray = false) { _available.Enqueue(bytes); Interlocked.Increment(ref _returns); }
    }

    [Fact]
    public void ReplacedAndTakenFramesReturnBuffersOnlyAfterOwnershipEnds()
    {
        var pool = new TrackingPool();
        var first = pool.Rent(4); var second = pool.Rent(4);
        using var blocked = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int calls = 0;
        var pump = new BrainDrainCapturePump(() => {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) return new BrainDrainCapturePump.Frame(1, 1, first, pool);
            if (call == 2) return new BrainDrainCapturePump.Frame(1, 1, second, pool);
            blocked.Set(); release.Wait(); return null;
        }, TimeSpan.FromMilliseconds(10));
        try
        {
            Assert.True(blocked.Wait(TimeSpan.FromSeconds(5))); // Both frames published.
            Assert.Equal(1, pool.Returns); // Replaced frame returned.
            var reused = pool.Rent(4); Assert.Same(first, reused); pool.Return(reused);
            using var taken = pump.Take(); Assert.NotNull(taken); Assert.Same(second, taken.Pixels);
            pump.Shutdown(); Assert.Equal(2, pool.Returns); // Taken frame is still in use.
            taken.Dispose(); taken.Dispose(); Assert.Equal(3, pool.Returns);
        }
        finally { pump.Shutdown(); release.Set(); }
    }

    [Fact]
    public void ShutdownReturnsPendingAndLateCaptureBuffers()
    {
        var pool = new TrackingPool();
        using var blocked = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int calls = 0;
        var pump = new BrainDrainCapturePump(() => {
            if (Interlocked.Increment(ref calls) == 1) return new BrainDrainCapturePump.Frame(1, 1, pool.Rent(4), pool);
            blocked.Set(); release.Wait(); return new BrainDrainCapturePump.Frame(1, 1, pool.Rent(4), pool);
        }, TimeSpan.FromMilliseconds(10));
        try
        {
            Assert.True(blocked.Wait(TimeSpan.FromSeconds(5)));
            pump.Shutdown(); Assert.Equal(1, pool.Returns); // Pending frame.
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => pool.Returns == 2, TimeSpan.FromSeconds(5))); // Late frame.
            Assert.Null(pump.Take());
        }
        finally { pump.Shutdown(); release.Set(); }
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
