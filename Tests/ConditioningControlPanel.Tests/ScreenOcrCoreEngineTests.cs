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

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("ru-RU")]
    [InlineData("zh-Hans-CN")]
    public async Task NonLatinWindowsProfileUsesProfileEngine(string profile)
    {
        var profileEngine = new FakeEngine { Text = "こんにちは" };
        var coreEngine = new FakeEngine();
        using var service = new ScreenOcrService(ScreenOcrService.SelectEngine(profile, () => profileEngine, () => coreEngine));
        var (text, words) = await service.DecodeAndRecognizeAsync(new Bitmap(20, 10, PixelFormat.Format32bppArgb),
            new Rectangle(-100, 200, 20, 10), null);
        Assert.Equal("こんにちは", text);
        Assert.Equal(new Rectangle(-97, 204, 5, 6), Assert.Single(words!).ScreenRect);
        Assert.NotNull(profileEngine.Frame);
        Assert.Null(coreEngine.Frame);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData(null)]
    public void EnglishOrMissingProfileUsesCore(string? profile)
    {
        var profileEngine = new FakeEngine();
        var coreEngine = new FakeEngine();
        using var selected = ScreenOcrService.SelectEngine(profile, () => profileEngine, () => coreEngine);
        Assert.Same(coreEngine, selected);
    }

    [Fact]
    public async Task WindowsJapaneseEngineReadsJapaneseTextWhenInstalled()
    {
        var language = new Windows.Globalization.Language("ja-JP");
        if (!Windows.Media.Ocr.OcrEngine.IsLanguageSupported(language))
        {
            Assert.Skip("Japanese Windows OCR language is not installed.");
            return;
        }
        var native = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(language);
        Assert.NotNull(native);
        using var service = new ScreenOcrService(new WindowsProfileOcrEngine(native!));
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
