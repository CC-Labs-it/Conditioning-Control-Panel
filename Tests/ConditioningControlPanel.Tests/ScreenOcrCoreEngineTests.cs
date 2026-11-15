using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ScreenOcrCoreEngineTests
{
    private sealed class FakeEngine : IScreenOcrEngine
    {
        public bool IsAvailable { get; set; } = true;
        public string Text { get; set; } = "hello";
        public ScreenFrame? Frame { get; private set; }
        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            Frame = frame;
            return new[] { new OcrWord(Text, new Rectangle(3, 4, 5, 6)) };
        }
        public void Dispose() { }
    }

    private sealed class BlockingEngine : IScreenOcrEngine
    {
        private readonly object _gate = new();
        public readonly ManualResetEventSlim Recognizing = new();
        public readonly ManualResetEventSlim Release = new();
        public readonly ManualResetEventSlim CheckingAvailability = new();
        public bool IsAvailable { get { CheckingAvailability.Set(); lock (_gate) return true; } }
        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Recognizing.Set();
                Release.Wait();
                return Array.Empty<OcrWord>();
            }
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task PanicStopDoesNotWaitForRestartDuringRecognition()
    {
        var engine = new BlockingEngine();
        using var service = new ScreenOcrService(engine);
        service.Start();
        var scan = Task.Run(() => service.DecodeAndRecognizeAsync(
            new Bitmap(20, 10, PixelFormat.Format32bppArgb), new Rectangle(0, 0, 20, 10), null));
        Task? restart = null;
        Task? panicStop = null;
        bool stoppedPromptly = false;
        try
        {
            Assert.True(engine.Recognizing.Wait(TimeSpan.FromSeconds(5)));
            service.Stop();
            engine.CheckingAvailability.Reset();
            restart = Task.Run(service.Start);
            Assert.True(engine.CheckingAvailability.Wait(TimeSpan.FromSeconds(5)));
            panicStop = Task.Run(service.Stop);
            stoppedPromptly = await Task.WhenAny(panicStop, Task.Delay(TimeSpan.FromSeconds(2))) == panicStop;
        }
        finally
        {
            engine.Release.Set();
            await scan;
            if (restart != null) await restart;
            if (panicStop != null) await panicStop;
        }
        Assert.True(stoppedPromptly, "Panic stop waited for OCR recognition");
        Assert.Null(typeof(ScreenOcrService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service));
    }

    [Fact]
    public void WindowsAvailabilityDoesNotWaitForRecognitionGate()
    {
        using var engine = new WindowsProfileOcrEngine(null!);
        var gate = typeof(WindowsProfileOcrEngine).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!;
        using var entered = new ManualResetEventSlim();
        Task<bool> availability;
        bool returned;
        lock (gate)
        {
            availability = Task.Run(() => { entered.Set(); return engine.IsAvailable; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            returned = availability.Wait(TimeSpan.FromSeconds(2));
        }
        Assert.True(returned, "Availability waited for Windows OCR recognition");
        Assert.True(availability.GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData(-100, 200)]
    [InlineData(1920, -300)]
    public async Task RecognitionUsesCoreAndMapsMonitorCoordinates(int x, int y)
    {
        var engine = new FakeEngine();
        using var service = new ScreenOcrService(engine);
        var bounds = new Rectangle(x, y, 20, 10);
        var (text, words) = await service.DecodeAndRecognizeAsync(new Bitmap(20, 10, PixelFormat.Format32bppArgb), bounds, null);
        Assert.Equal("hello", text);
        Assert.Equal(new Rectangle(x + 3, y + 4, 5, 6), Assert.Single(words!).ScreenRect);
        Assert.Equal(ScreenPixelFormat.Bgra32, engine.Frame!.Format);
        Assert.Equal(bounds, engine.Frame.ScreenBounds);
    }

    [Theory, InlineData("en-US"), InlineData("ja-JP")]
    public void InstalledWindowsProfileSelectsWindowsEngine(string tag)
    {
        var language = new Windows.Globalization.Language(tag);
        if (!Windows.Media.Ocr.OcrEngine.IsLanguageSupported(language))
        {
            if (tag == "ja-JP") Assert.NotEqual("1", Environment.GetEnvironmentVariable("CCP_REQUIRE_JAPANESE_OCR"));
            Assert.Skip($"{tag} Windows OCR language is not installed.");
            return;
        }
        var native = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(language);
        Assert.NotNull(native);
        using var engine = ScreenOcrService.CreateEngine(() => native);
        var windows = Assert.IsType<WindowsProfileOcrEngine>(engine);
        Assert.Same(native, typeof(WindowsProfileOcrEngine).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(windows));
        Assert.True(windows.IsAvailable);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void MissingOrFailedWindowsProfileLeavesScanningUnavailable(bool throws)
    {
        using var service = new ScreenOcrService(ScreenOcrService.CreateEngine(() => throws ? throw new InvalidOperationException() : null));
        Assert.False(service.IsOcrAvailable);
        service.Start();
        Assert.Null(typeof(ScreenOcrService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service));
    }

    [Fact]
    public async Task WindowsJapaneseEngineReadsJapaneseTextWhenInstalled()
    {
        var language = new Windows.Globalization.Language("ja-JP");
        if (!Windows.Media.Ocr.OcrEngine.IsLanguageSupported(language))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("CCP_REQUIRE_JAPANESE_OCR"));
            Assert.Skip("Japanese Windows OCR language is not installed.");
            return;
        }
        var native = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(language);
        Assert.NotNull(native);
        using var service = new ScreenOcrService(ScreenOcrService.CreateEngine(() => native));
        var bitmap = new Bitmap(500, 160, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Yu Gothic", 64, FontStyle.Bold))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("日本語", font, Brushes.Black, new PointF(10, 10));
        }
        var (text, _) = await service.DecodeAndRecognizeAsync(bitmap, new Rectangle(0, 0, 500, 160), null);
        Assert.Contains("日本", text!.Replace(" ", ""));
    }

    [Fact]
    public void MissingModelsDoNotStartScanning()
    {
        var engine = new FakeEngine { IsAvailable = false };
        using var service = new ScreenOcrService(engine);
        Assert.False(service.IsOcrAvailable);
        service.Start();
        Assert.Null(typeof(ScreenOcrService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service));
        Assert.Null(engine.Frame);
    }
}
