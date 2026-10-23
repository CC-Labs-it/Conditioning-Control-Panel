using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class BrainDrainCaptureTests
{
    internal static BrainDrainCapturePump.Input Input(PixelRect? bounds = null, int strength = 100, bool melt = false) =>
        new(bounds ?? new PixelRect(0, 0, 64, 64), strength, melt, 4, 30, new[] { (IntPtr)123 });
    private sealed class FakeCapture : IDesktopCapture
    {
        internal SKImage? Last;
        internal IntPtr[]? Excluded;
        internal PixelRect Bounds;
        internal Action? During;
        internal bool Disposed, Fail, Black;
        internal byte Serial;
        public SKImage Capture(PixelRect bounds, int downscale, IntPtr[] excluded)
        {
            Bounds = bounds; Excluded = excluded; During?.Invoke();
            if (Fail) throw new InvalidOperationException("fake capture failure");
            using var surface = SKSurface.Create(new SKImageInfo(16, 16));
            surface.Canvas.Clear(Black ? SKColors.Black : new SKColor(255, 0, ++Serial));
            return Last = surface.Snapshot();
        }
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public void NativeUndefinedAlphaBecomesOpaqueWithoutSwappingRedAndBlue()
    {
        var data = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.Copy(new byte[] { 0, 0, 255, 0, 255, 0, 0, 77 }, 0, data, 8);
            using var image = DesktopCapture.OpaqueBgr(data, 2, 1, 8);
            using var pixels = SKBitmap.FromImage(image);
            Assert.Equal(SKColors.Red, pixels.GetPixel(0, 0));
            Assert.Equal(SKColors.Blue, pixels.GetPixel(1, 0));
        }
        finally { Marshal.FreeHGlobal(data); }
    }
    [Fact]
    public void X11DepthTwentyFourDrawsInPlaceBeneathWithoutSwappingRedAndBlue()
    {
        var data = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.Copy(new byte[] { 0, 0, 255, 0, 255, 0, 0, 77 }, 0, data, 8);
            using var surface = SKSurface.Create(new SKImageInfo(2, 1));
            surface.Canvas.Clear(SKColors.Transparent);
            Assert.True(DesktopCapture.DrawBgr(surface.Canvas, data, 2, 1, 8, true, 0, 0, 255));
            using var top = new SKPaint { Color = SKColors.Lime };
            surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.DrawPoint(1, 0, top);
            DesktopCapture.DrawBgr(surface.Canvas, data, 2, 1, 8, true, 0, 0, 255);
            using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image);
            Assert.Equal(SKColors.Red, pixels.GetPixel(0, 0));
            Assert.Equal(SKColors.Lime, pixels.GetPixel(1, 0)); // lower window never paints over the upper one
        }
        finally { Marshal.FreeHGlobal(data); }
    }
    [Fact]
    public void X11ReadsOnlyTheOnScreenUncoveredPart()
    {
        var screen = new SKRectI(0, 0, 100, 100);
        using var covered = new SKRegion(new SKRectI(0, 0, 100, 50));
        Assert.Equal(new SKRectI(10, 50, 100, 100), X11DesktopCapture.Visible(covered, new SKRectI(10, 20, 300, 400), screen));
        Assert.Null(X11DesktopCapture.Visible(covered, new SKRectI(10, 0, 60, 50), screen)); // fully covered: no read
        Assert.Null(X11DesktopCapture.Visible(covered, new SKRectI(200, 0, 300, 100), screen)); // off screen: no read
    }
    [Fact]
    public void LatestFrameReplacesOwnedImageAndStopRetiresMailbox()
    {
        using var capture = new FakeCapture();
        using var pump = new BrainDrainCapturePump(Input(), () => capture, start: false);
        pump.CaptureOnce(capture);
        Assert.Equal(IntPtr.Zero, capture.Last!.Handle); // raw capture never crosses threads
        using var first = pump.Take();
        Assert.NotNull(first);
        pump.CaptureOnce(capture);
        var replaced = pump.PendingImageForTest!;
        pump.CaptureOnce(capture); // replaces pending, not the UI-owned first
        Assert.Equal(IntPtr.Zero, replaced.Handle);
        Assert.NotEqual(IntPtr.Zero, first.Image.Handle);
        using var newest = pump.Take();
        Assert.NotNull(newest); Assert.Null(pump.Take());
        using var newestPixels = SKBitmap.FromImage(newest.Image);
        Assert.Equal((byte)3, newestPixels.GetPixel(8, 8).Blue);
        pump.CaptureOnce(capture); var abandoned = pump.PendingImageForTest!;
        pump.Dispose(); Assert.Equal(IntPtr.Zero, abandoned.Handle); pump.CaptureOnce(capture);
        Assert.Null(pump.Take()); Assert.True(pump.Stopped);
        Assert.NotEqual(IntPtr.Zero, newest.Image.Handle);
    }
    [Fact]
    public void TargetChangeDiscardsInFlightAndQueuedOldScreenAndPreservesExclusion()
    {
        using var capture = new FakeCapture();
        using var pump = new BrainDrainCapturePump(Input(), () => capture, start: false);
        pump.CaptureOnce(capture);
        var next = Input(new PixelRect(100, -50, 80, 64));
        pump.Update(next); Assert.Null(pump.Take());
        capture.During = () => pump.Update(Input(new PixelRect(200, 0, 80, 64)));
        pump.CaptureOnce(capture); Assert.Null(pump.Take());
        capture.During = null; pump.Update(next); pump.CaptureOnce(capture);
        using var frame = pump.Take();
        Assert.Equal(next.Bounds, frame!.Input.Bounds);
        Assert.Equal(next.Bounds, capture.Bounds);
        Assert.Equal(new[] { (IntPtr)123 }, capture.Excluded);
    }
    [Fact]
    public void ZeroFrameTimeoutAndCaptureFailureNeverReportSuccessfulBlur()
    {
        double now = 0;
        using var capture = new FakeCapture();
        using var pump = new BrainDrainCapturePump(Input(), () => capture, () => now, start: false);
        Assert.Null(pump.Notice); now = 3;
        Assert.Contains("no fresh desktop frame", pump.Notice);
        pump.CaptureOnce(capture); Assert.Null(pump.Notice);
        capture.Fail = true; pump.CaptureOnce(capture);
        Assert.Null(pump.Take()); Assert.Null(pump.Notice); // one bad frame keeps the last blur
        now = 6; Assert.Contains("fake capture failure", pump.Notice);
        capture.Fail = false; capture.Black = true; pump.CaptureOnce(capture);
        Assert.Null(pump.Take()); Assert.Contains("black/empty", pump.Notice);
        capture.Black = false; pump.CaptureOnce(capture); now = 10;
        Assert.Null(pump.Take()); Assert.Contains("no fresh", pump.Notice);
    }
    [Fact]
    public void StopDoesNotJoinStalledWorkerAndWorkerDisposesItsOwnResources()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var disposed = new ManualResetEventSlim();
        int thread = 0;
        var capture = new BlockingCapture(entered, release, disposed);
        using var pump = new BrainDrainCapturePump(Input(), () => { thread = Environment.CurrentManagedThreadId; return capture; });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            pump.Dispose(); pump.Dispose(); // must return while worker is still blocked
            Assert.False(disposed.IsSet); Assert.Null(pump.Take());
            Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
        }
        finally { release.Set(); }
        Assert.True(disposed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(thread, capture.DisposeThread);
    }
    private sealed class BlockingCapture(ManualResetEventSlim entered, ManualResetEventSlim release, ManualResetEventSlim disposed) : IDesktopCapture
    {
        internal int DisposeThread;
        public SKImage Capture(PixelRect bounds, int downscale, IntPtr[] excluded)
        { entered.Set(); release.Wait(); using var s = SKSurface.Create(new SKImageInfo(16, 16)); s.Canvas.Clear(SKColors.Red); return s.Snapshot(); }
        public void Dispose() { DisposeThread = Environment.CurrentManagedThreadId; disposed.Set(); }
    }
    [Fact]
    public void LiveIntensityMeltAndRefreshChangeOutputWithoutRestart()
    {
        using var rawSurface = SKSurface.Create(new SKImageInfo(64, 64));
        rawSurface.Canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = SKColors.White };
        rawSurface.Canvas.DrawRect(32, 0, 32, 64, paint);
        using var raw = rawSurface.Snapshot();
        using var plain = BrainDrainCapturePump.Blur(raw, Input(strength: 0), 0);
        using var blur = BrainDrainCapturePump.Blur(raw, Input(), 0);
        using var melt = BrainDrainCapturePump.Blur(raw, Input(melt: true), 1);
        using var moved = BrainDrainCapturePump.Blur(raw, Input(melt: true), 2);
        using var p = SKBitmap.FromImage(plain); using var b = SKBitmap.FromImage(blur);
        Assert.Equal((byte)0, p.GetPixel(31, 32).Red);
        Assert.InRange(b.GetPixel(31, 32).Red, (byte)1, (byte)254);
        using var bd = blur.Encode(); using var md = melt.Encode(); using var nd = moved.Encode();
        Assert.False(bd.ToArray().SequenceEqual(md.ToArray())); Assert.False(md.ToArray().SequenceEqual(nd.ToArray()));
        Assert.Equal(0, BrainDrainCapturePump.Alpha(0)); Assert.Equal(.85, BrainDrainCapturePump.Alpha(200), 5);
        using var capture = new FakeCapture();
        using var pump = new BrainDrainCapturePump(Input(), () => capture, start: false);
        var live = Input(strength: 40, melt: true) with { Fps = 15, Downscale = 8 };
        pump.Update(live); pump.CaptureOnce(capture); using var frame = pump.Take();
        Assert.Same(live, frame!.Input);
    }
    [Fact]
    public void VisualDisplaysFreshFrameAndRemovesItOnCaptureFailure() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        double now = 0;
        using var capture = new FakeCapture();
        using var visual = new BrainDrainVisual();
        visual.Start(Input(), () => capture, startWorker: false, () => now);
        visual.Pump!.CaptureOnce(capture); visual.Step();
        var image = Assert.Single(visual.Children.OfType<Image>());
        Assert.NotNull(image.Source); Assert.False(visual.Veil.IsVisible); Assert.False(visual.Notice.IsVisible);
        capture.Fail = true; visual.Pump.CaptureOnce(capture); visual.Step();
        Assert.NotNull(image.Source); Assert.False(visual.Veil.IsVisible); // no flicker on one bad frame
        now = 3; visual.Step();
        Assert.Null(image.Source); Assert.True(visual.Veil.IsVisible);
        Assert.Contains("fake capture failure", visual.Notice.Text);
    });
    [Fact]
    public void WaylandMakesNoCaptureAttemptAndShowsVisibleVeilNotice() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Assert.Null(DesktopCapture.Factory(OverlayBackend.None));
        using var visual = new BrainDrainVisual();
        visual.Start(Input(), DesktopCapture.Factory(OverlayBackend.None));
        visual.Step();
        Assert.True(visual.Veil.IsVisible); Assert.True(visual.Notice.IsVisible);
        Assert.Contains("native Wayland", visual.Notice.Text);
        Assert.Equal((byte)70, Assert.IsType<global::Avalonia.Media.SolidColorBrush>(visual.Veil.Background).Color.A);
    });
    [Fact]
    public void PanicEntryStopsIdempotently() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var prior = (settings.BrainDrainEnabled, settings.BrainDrainBlurStrength);
        var provider = ConditioningControlPanel.CoreSession.IsEngineRunningProvider;
        var panel = new Grid();
        var host = new Window { Content = panel };
        try
        {
            ConditioningControlPanel.CoreSession.IsEngineRunningProvider = () => true;
            settings.BrainDrainEnabled = true; settings.BrainDrainBlurStrength = 50;
            BrainDrainOverlay.Refresh(host); BrainDrainOverlay.Refresh(host);
            var visual = Assert.Single(panel.Children.OfType<BrainDrainVisual>());
            Assert.Contains("in this app only", visual.Notice.Text);
            var pump = visual.Pump!;
            var stop = Assert.Single(PanicSurfaces.All.Where(s => s.Id == "brain-drain"));
            stop.Stop(null); stop.Stop(null);
            Assert.True(pump.Stopped); Assert.Empty(panel.Children);
            Assert.Equal(0, BrainDrainOverlay.WindowCount);
        }
        finally
        {
            BrainDrainOverlay.Stop(); host.Close();
            (settings.BrainDrainEnabled, settings.BrainDrainBlurStrength) = prior;
            ConditioningControlPanel.CoreSession.IsEngineRunningProvider = provider;
        }
    });
}
