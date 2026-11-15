using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using SkiaSharp;
using Tmds.DBus.Protocol;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ScreenFrameSourceTests
{
    [Fact]
    public async Task CapturesEachMonitorWithItsPhysicalOriginAndScale()
    {
        CaptureMonitor[] monitors = [new("left", new Rectangle(-2, 0, 2, 1), 1), new("right", new Rectangle(0, 0, 2, 1), 2)];
        var calls = new List<CaptureMonitor>();
        var source = new DesktopScreenFrames(m => { calls.Add(m); return new byte[m.Bounds.Width * m.Bounds.Height * 4]; },
            () => Task.FromResult<IReadOnlyList<CaptureMonitor>>(monitors));
        var result = await source.CaptureAsync(CancellationToken.None);
        Assert.Equal(ScreenFrameStatus.Available, result.Status);
        Assert.Equal(monitors, calls);
        Assert.Equal(new Rectangle(-1, 0, 1, 1), result.Frames[0].ToScreen(new Rectangle(1, 0, 1, 1)));
        Assert.Equal(new Rectangle(1, 0, 1, 1), result.Frames[1].ToScreen(new Rectangle(1, 0, 1, 1)));
        Assert.Equal(2, result.Frames[1].Scale);
    }

    [Fact]
    public async Task AFailedMonitorIsUnavailableRatherThanPartial()
    {
        var source = new DesktopScreenFrames(_ => null, () => Task.FromResult<IReadOnlyList<CaptureMonitor>>(
            [new("screen", new Rectangle(0, 0, 1, 1), 1)]));
        var result = await source.CaptureAsync(CancellationToken.None);
        Assert.Equal(ScreenFrameStatus.Unavailable, result.Status);
        Assert.Empty(result.Frames);
    }

    private sealed class FakeEngine : IScreenOcrEngine
    {
        public bool IsAvailable => true;
        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default) =>
            [new(frame.Screen + "-word", new Rectangle(1, 0, 1, 1))];
        public void Dispose() { }
    }

    [Fact]
    public async Task ScanReadsEveryMonitorAndMapsWordsToTheDesktop()
    {
        var source = new DesktopScreenFrames(m => new byte[m.Bounds.Width * m.Bounds.Height * 4],
            () => Task.FromResult<IReadOnlyList<CaptureMonitor>>([new("left", new Rectangle(-2, 0, 2, 1), 1), new("right", new Rectangle(0, 0, 2, 1), 2)]));
        var words = await ScreenOcrService.Scan(source, new FakeEngine())(CancellationToken.None);
        Assert.Equal([new ScreenOcrService.Word("left-word", new Rectangle(-1, 0, 1, 1), "left"),
            new ScreenOcrService.Word("right-word", new Rectangle(1, 0, 1, 1), "right")], words);
        Assert.Empty(await ScreenOcrService.Scan(new UnavailableScreenFrames(), new FakeEngine())(CancellationToken.None));
    }

    private sealed class CountingEngine : IScreenOcrEngine
    {
        public readonly List<string> Calls = [];
        public bool IsAvailable => true;
        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            Calls.Add(frame.Screen);
            return [new($"{frame.Screen}-{Calls.Count}", new Rectangle(0, 0, 1, 1))];
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task UnchangedMonitorsReuseTheirWordsUntilTheScannerStops()
    {
        byte left = 0;
        var source = new DesktopScreenFrames(m => { var px = new byte[m.Bounds.Width * m.Bounds.Height * 4]; if (m.Name == "left") px[0] = left; return px; },
            () => Task.FromResult<IReadOnlyList<CaptureMonitor>>([new("left", new Rectangle(0, 0, 2, 1), 1), new("right", new Rectangle(2, 0, 2, 1), 1)]));
        var engine = new CountingEngine();
        var scan = ScreenOcrService.Scan(source, engine);
        Assert.Equal(["left-1", "right-2"], (await scan(CancellationToken.None)).Select(w => w.Text));
        Assert.Equal(["left-1", "right-2"], (await scan(CancellationToken.None)).Select(w => w.Text)); // same words re-delivered
        Assert.Equal(2, engine.Calls.Count);
        left = 9; // only the left monitor changed
        Assert.Equal(["left-3", "right-2"], (await scan(CancellationToken.None)).Select(w => w.Text));
        Assert.Equal(["left", "right", "left"], engine.Calls);
        new ScreenOcrService(_ => Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([]), () => true, (_, _) => Task.FromResult(true)).Stop();
        await scan(CancellationToken.None);
        Assert.Equal(5, engine.Calls.Count); // a stop drops the cache: both monitors are read again
    }

    private sealed class NoModels : IScreenOcrEngine
    {
        public bool IsAvailable => false;
        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default) => [];
        public void Dispose() { }
    }

    [Fact]
    public void WaylandGetsThePortalSourceAndOnlyMissingCaptureOrModelsLeaveItNull()
    {
        var saved = (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), KeywordTriggerHead.OcrSource);
        try
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-0");
            var frames = ScreenFrameSourceFactory.Create();
            if (OperatingSystem.IsLinux()) Assert.IsType<PortalScreenFrames>(frames); // also under the test sandbox (Flatpak-like)
            Assert.NotNull(ScreenOcrService.CreateSource(new PortalScreenFrames(), new FakeEngine()));
            Assert.Null(ScreenOcrService.CreateSource(new PortalScreenFrames(), new NoModels()));
            KeywordTriggerHead.OcrSource = null;
            Assert.Equal("ocr_unavailable_models", ScreenOcrService.UnavailableReasonKey());
            Assert.Null(ScreenOcrService.CreateSource(new UnavailableScreenFrames(), new FakeEngine()));
            Assert.Equal("ocr_unavailable_capture", ScreenOcrService.UnavailableReasonKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", saved.Item1);
            KeywordTriggerHead.OcrSource = saved.OcrSource;
            ScreenOcrService.CreateSource(new PortalScreenFrames(), new FakeEngine()); // clears the static reason
        }
    }

    // Portal tests share the session-wide PortalScreenFrames.Refused flag; xunit runs one class's tests in sequence.
    private static Task<IReadOnlyList<CaptureMonitor>> TwoMonitors() => Task.FromResult<IReadOnlyList<CaptureMonitor>>(
        [new("left", new Rectangle(0, 0, 2, 2), 1), new("right", new Rectangle(2, 1, 1, 1), 2)]);

    private static string WritePng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-portal-{Guid.NewGuid():N}.png");
        using var bmp = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int x = 0; x < 3; x++) for (int y = 0; y < 2; y++) bmp.SetPixel(x, y, new SKColor((byte)(x * 100), (byte)(y * 100), 7));
        using (var file = File.Create(path)) bmp.Encode(file, SKEncodedImageFormat.Png, 100);
        return path;
    }

    private static Dictionary<string, VariantValue> Uri(string path) => new() { ["uri"] = new System.Uri(path).AbsoluteUri };

    [Fact]
    public async Task PortalAnswerIsSplitPerMonitorAndTheScreenshotDeleted()
    {
        PortalScreenFrames.Refused = false;
        var path = WritePng();
        var source = new PortalScreenFrames(_ => Task.FromResult((0u, Uri(path))), TwoMonitors);
        var result = await source.CaptureAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
        Assert.Equal(ScreenFrameStatus.Available, result.Status);
        Assert.Equal((2, 2), (result.Frames[0].Width, result.Frames[0].Height));
        Assert.Equal((1, 1, new Rectangle(2, 1, 1, 1), 2.0), (result.Frames[1].Width, result.Frames[1].Height, result.Frames[1].ScreenBounds, result.Frames[1].Scale));
        // Pixel (2,1) of the shot is B=7 G=100 R=200 in BGRA order.
        Assert.Equal(new byte[] { 7, 100, 200, 255 }, result.Frames[1].Pixels.ToArray());
        Assert.Null(PortalScreenFrames.FilePath(new() { ["uri"] = "https://example.com/x.png" }));
        Assert.Null(PortalScreenFrames.FilePath(new()));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public async Task APortalRefusalIsUnavailableForTheSessionAndNeverAsksAgain(uint code)
    {
        PortalScreenFrames.Refused = false;
        var calls = 0;
        var source = new PortalScreenFrames(_ => { calls++; return Task.FromResult((code, new Dictionary<string, VariantValue>())); }, TwoMonitors);
        Assert.Equal(ScreenFrameStatus.Unavailable, (await source.CaptureAsync(CancellationToken.None)).Status);
        Assert.True(PortalScreenFrames.Refused);
        Assert.Equal(ScreenFrameStatus.Unavailable, (await new PortalScreenFrames(_ => { calls++; return Task.FromResult((0u, new Dictionary<string, VariantValue>())); }, TwoMonitors)
            .CaptureAsync(CancellationToken.None)).Status);
        Assert.Equal(1, calls);
        PortalScreenFrames.Refused = false;
    }

    [Fact]
    public async Task APendingPortalCallTimesOutThenSkipsScansAndALateFileIsStillDeleted()
    {
        PortalScreenFrames.Refused = false;
        var answer = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>();
        var calls = 0;
        var source = new PortalScreenFrames(_ => { calls++; return answer.Task; }, TwoMonitors, TimeSpan.FromMilliseconds(50));
        Assert.Equal(ScreenFrameStatus.Unavailable, (await source.CaptureAsync(CancellationToken.None)).Status); // timed out
        Assert.Equal(ScreenFrameStatus.Unavailable, (await source.CaptureAsync(CancellationToken.None)).Status); // skipped
        Assert.Equal(1, calls);
        Assert.False(PortalScreenFrames.Refused);
        var path = WritePng();
        answer.SetResult((0u, Uri(path)));
        for (int i = 0; i < 100 && File.Exists(path); i++) await Task.Delay(20);
        Assert.False(File.Exists(path));
        var next = WritePng();
        answer = new();
        answer.SetResult((0u, Uri(next)));
        var again = await source.CaptureAsync(CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal(ScreenFrameStatus.Available, again.Status); // the timeout cost one scan, not the session
    }

    [Fact]
    public async Task AScreenshotResponseLongAfterTheScanTimeoutIsStillAwaitedAndItsFileDeleted()
    {
        PortalScreenFrames.Refused = false;
        var answer = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>();
        var closes = 0;
        var source = new PortalScreenFrames(ct => PortalPanicShortcut.AwaitOrCloseAsync(answer.Task, ct, () => { closes++; return Task.CompletedTask; }),
            TwoMonitors, TimeSpan.FromMilliseconds(50));
        Assert.Equal(ScreenFrameStatus.Unavailable, (await source.CaptureAsync(CancellationToken.None)).Status);
        await Task.Delay(400);
        var path = WritePng();
        answer.SetResult((0u, Uri(path)));
        for (int i = 0; i < 100 && File.Exists(path); i++) await Task.Delay(20);
        Assert.False(File.Exists(path));
        Assert.Equal(0, closes);
    }

    [Fact]
    public async Task ACancelledPortalRequestIsClosedAndAResponseAlreadyInFlightStillWins()
    {
        var raced = new TaskCompletionSource<int>();
        using var cts = new CancellationTokenSource();
        var waiting = PortalPanicShortcut.AwaitOrCloseAsync(raced.Task, cts.Token, () => { raced.SetResult(7); return Task.CompletedTask; });
        cts.Cancel();
        Assert.Equal(7, await waiting);

        var closes = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PortalPanicShortcut.AwaitOrCloseAsync(new TaskCompletionSource<int>().Task, new CancellationToken(true), () => { closes++; return Task.CompletedTask; }));
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task StoppingTheScanClosesThePortalRequestAndAnInFlightFileIsDeleted()
    {
        PortalScreenFrames.Refused = false;
        var answer = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>();
        var path = WritePng();
        var closes = 0;
        var source = new PortalScreenFrames(ct => PortalPanicShortcut.AwaitOrCloseAsync(answer.Task, ct,
            () => { closes++; answer.SetResult((0u, Uri(path))); return Task.CompletedTask; }), TwoMonitors);
        using var stop = new CancellationTokenSource();
        var scan = source.CaptureAsync(stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        for (int i = 0; i < 100 && File.Exists(path); i++) await Task.Delay(20);
        Assert.Equal(1, closes);
        Assert.False(File.Exists(path));
        Assert.False(PortalScreenFrames.Refused);
    }

    [Fact]
    public async Task AFailedRequestCloseKeepsObservingAndALateFileIsStillDeleted()
    {
        PortalScreenFrames.Refused = false;
        var answer = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>();
        var source = new PortalScreenFrames(ct => PortalPanicShortcut.AwaitOrCloseAsync(answer.Task, ct,
            () => Task.FromException(new InvalidOperationException("close failed"))), TwoMonitors);
        using var stop = new CancellationTokenSource();
        var scan = source.CaptureAsync(stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        await Task.Delay(1500); // past the one-second in-flight window that follows a successful Close
        var path = WritePng();
        answer.SetResult((0u, Uri(path)));
        for (int i = 0; i < 100 && File.Exists(path); i++) await Task.Delay(20);
        Assert.False(File.Exists(path));
    }
}
