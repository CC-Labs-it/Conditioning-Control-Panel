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
        public ScreenFrame? Frame { get; private set; }
        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            Frame = frame;
            return new[] { new OcrWord("hello", new Rectangle(3, 4, 5, 6)) };
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
