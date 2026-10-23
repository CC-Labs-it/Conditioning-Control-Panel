using System;
using System.Runtime.InteropServices;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainX11Tests
{
    [Fact]
    public void IsolatedX11CapturesContentBlursAndExcludesCoveringOverlay()
    {
        // Never touch the user's desktop. Explicit opt-in must name the isolated dummy X server.
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CCP_BRAIN_X11_TEST") == "1", "Requires isolated X11 test server");
        Assert.Equal(":99", Environment.GetEnvironmentVariable("DISPLAY"));
        XInitThreads();
        Assert.True(X11Overlay.IsAvailable); // installs existing process X error handler
        var d = XOpenDisplay(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, d);
        var root = XDefaultRootWindow(d);
        var background = XCreateSimpleWindow(d, root, 0, 0, 128, 64, 0, 0, 0xff0000);
        var cover = XCreateSimpleWindow(d, root, 0, 0, 128, 64, 0, 0, 0x00ff00);
        try
        {
            using var capture = new X11DesktopCapture();
            XMapWindow(d, background); XMapWindow(d, cover);
            var gc = XCreateGC(d, background, 0, IntPtr.Zero);
            try { XSetForeground(d, gc, 0x0000ff); XFillRectangle(d, background, gc, 64, 0, 64, 64); }
            finally { XFreeGC(d, gc); }
            XSync(d, false);
            var input = BrainDrainCaptureTests.Input(new PixelRect(0, 0, 128, 64)) with { Downscale = 1, Excluded = new[] { cover } };
            using var raw = capture.Capture(input.Bounds, 1, input.Excluded);
            using var pixels = SKBitmap.FromImage(raw);
            Assert.Equal(SKColors.Red, pixels.GetPixel(10, 32)); Assert.Equal(SKColors.Blue, pixels.GetPixel(100, 32));
            using var target = capture.Capture(new PixelRect(64, 0, 64, 64), 4, input.Excluded);
            Assert.Equal(16, target.Width);
            using var targetPixels = SKBitmap.FromImage(target);
            Assert.Equal(SKColors.Blue, targetPixels.GetPixel(8, 8));
            using var blurred = BrainDrainCapturePump.Blur(raw, input, 0);
            using var blurPixels = SKBitmap.FromImage(blurred);
            Assert.InRange(blurPixels.GetPixel(63, 32).Blue, (byte)1, (byte)254);
            Assert.Equal((byte)0, blurPixels.GetPixel(63, 32).Green); // covering green overlay was never captured
            using var included = capture.Capture(input.Bounds, 1, Array.Empty<IntPtr>());
            using var coverPixels = SKBitmap.FromImage(included);
            Assert.Equal(SKColors.Lime, coverPixels.GetPixel(10, 32)); // proves exclusion changes actual native pixels
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 30; i++) capture.Capture(input.Bounds, 4, input.Excluded).Dispose();
            TestContext.Current.TestOutputHelper?.WriteLine($"brain-drain-x11: {clock.Elapsed.TotalMilliseconds / 30:F2} ms/frame (128x64, downscale 4)");
        }
        finally { XDestroyWindow(d, cover); XDestroyWindow(d, background); XCloseDisplay(d); }
    }
    private const string X = "libX11.so.6";
    [DllImport(X)] private static extern int XInitThreads();
    [DllImport(X)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X)] private static extern int XCloseDisplay(IntPtr d);
    [DllImport(X)] private static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport(X)] private static extern IntPtr XCreateSimpleWindow(IntPtr d, IntPtr parent, int x, int y, uint w, uint h, uint border, nuint borderColor, nuint background);
    [DllImport(X)] private static extern int XMapWindow(IntPtr d, IntPtr w);
    [DllImport(X)] private static extern int XDestroyWindow(IntPtr d, IntPtr w);
    [DllImport(X)] private static extern IntPtr XCreateGC(IntPtr d, IntPtr w, nuint mask, IntPtr values);
    [DllImport(X)] private static extern int XFreeGC(IntPtr d, IntPtr gc);
    [DllImport(X)] private static extern int XSetForeground(IntPtr d, IntPtr gc, nuint color);
    [DllImport(X)] private static extern int XFillRectangle(IntPtr d, IntPtr w, IntPtr gc, int x, int y, uint width, uint height);
    [DllImport(X)] private static extern int XSync(IntPtr d, bool discard);
}
