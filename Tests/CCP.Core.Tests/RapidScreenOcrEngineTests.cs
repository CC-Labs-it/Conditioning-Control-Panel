using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using SkiaSharp;
using Xunit;

namespace CCP.Core.Tests;

// Runs on the models CCP.Core ships into this test output (models/v5), so CI exercises the real engine.
public class RapidScreenOcrEngineTests(ITestOutputHelper output)
{
    private static readonly RapidScreenOcrEngine Engine = new();

    // White 1920x1080 BGRA frame; text drawn black at (x, baseline) with SKFont(SKTypeface.Default, 48) (SkiaSharp 4: explicit face).
    private static (ScreenFrame Frame, SKRect Drawn) Frame(string? text, float x = 300, float y = 400)
    {
        var info = new SKImageInfo(1920, 1080, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            if (text != null)
            {
                using var font = new SKFont(SKTypeface.Default, 48);
                using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                canvas.DrawText(text, x, y, font, paint);
            }
        }
        var drawn = SKRect.Empty;
        if (text != null)
        {
            using var font = new SKFont(SKTypeface.Default, 48);
            font.MeasureText(text, out drawn);
            drawn.Offset(x, y);
        }
        return (new ScreenFrame("test", new Rectangle(0, 0, 1920, 1080), 1, 1920, 1080, info.RowBytes,
            ScreenPixelFormat.Bgra32, bitmap.Bytes), drawn);
    }

    [Fact]
    public void DrawnWordsAreReadWithBoxesInsideTheDrawnArea()
    {
        var (frame, drawn) = Frame("Good girl obey 2026");
        Assert.True(Engine.IsAvailable);
        Engine.Recognize(Frame(null).Frame); // model load is not part of the per-frame cost
        var sw = Stopwatch.StartNew();
        var words = Engine.Recognize(frame);
        output.WriteLine($"{sw.ElapsedMilliseconds} ms: " + string.Join(" | ", words.Select(w => $"{w.Text} {w.Bounds}")));
        Assert.Equal(new[] { "Good", "girl", "obey", "2026" }, words.Select(w => w.Text));
        // DBNet unclips its boxes beyond the glyphs, so allow half an em around the drawn text; the centre must be on it.
        var area = Rectangle.FromLTRB((int)drawn.Left - 24, (int)drawn.Top - 24, (int)drawn.Right + 24, (int)drawn.Bottom + 24);
        Assert.All(words, w => Assert.True(area.Contains(w.Bounds) &&
            drawn.Contains(w.Bounds.X + w.Bounds.Width / 2f, w.Bounds.Y + w.Bounds.Height / 2f), $"{w.Text} {w.Bounds} outside {drawn}"));
        Assert.True(words.Select(w => w.Bounds.Left).SequenceEqual(words.Select(w => w.Bounds.Left).Order()));
    }

    [Fact]
    public void BlankFrameHasNoWords() => Assert.Empty(Engine.Recognize(Frame(null).Frame));

    [Fact]
    public void MissingModelsAreUnavailableWithoutThrowing()
    {
        using var engine = new RapidScreenOcrEngine(Path.Combine(Path.GetTempPath(), "ccp-no-ocr-models-" + Guid.NewGuid()));
        Assert.False(engine.IsAvailable);
        Assert.Empty(engine.Recognize(Frame("Good girl").Frame));
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void AvailabilityDoesNotWaitForRecognitionGate()
    {
        using var engine = new RapidScreenOcrEngine();
        var gate = typeof(RapidScreenOcrEngine).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!;
        using var entered = new ManualResetEventSlim();
        Task<bool> availability;
        bool returned;
        lock (gate)
        {
            availability = Task.Run(() => { entered.Set(); return engine.IsAvailable; });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            returned = availability.Wait(TimeSpan.FromSeconds(2));
        }
        Assert.True(returned, "Availability waited for recognition to finish");
        Assert.True(availability.GetAwaiter().GetResult());
    }

    [Fact]
    public void DisposeDuringRecognitionDoesNotWaitAndReleasesSessionsAfterIt()
    {
        var engine = new RapidScreenOcrEngine();
        var ocr = typeof(RapidScreenOcrEngine).GetField("_ocr", BindingFlags.NonPublic | BindingFlags.Instance)!;
        engine.Recognize(Frame(null).Frame); // loads the sessions
        Assert.NotNull(ocr.GetValue(engine));
        using var inDetect = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        engine.BeforeDetect = () => { inDetect.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
        var scan = Task.Run(() => engine.Recognize(Frame(null).Frame));
        Assert.True(inDetect.Wait(TimeSpan.FromSeconds(5)));

        var sw = Stopwatch.StartNew();
        engine.Dispose();
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 50, $"Dispose blocked {sw.ElapsedMilliseconds} ms on the active recognition");
        Assert.False(engine.IsAvailable);
        Assert.NotNull(ocr.GetValue(engine)); // still in use by the active scan

        release.Set();
        Assert.True(scan.Wait(TimeSpan.FromSeconds(10)));
        Assert.Null(ocr.GetValue(engine));
        engine.BeforeDetect = null;
        Assert.Empty(engine.Recognize(Frame("Good girl obey").Frame));
        Assert.Null(ocr.GetValue(engine));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void InstalledProfileWinsWithoutCoreModels(string language)
    {
        using var native = new RapidScreenOcrEngine();
        using var missing = new RapidScreenOcrEngine(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.True(native.IsAvailable);
        Assert.False(missing.IsAvailable);
        Assert.Same(native, ScreenOcrEngineSelection.Select(language, () => native, () => missing));
    }

    [Fact]
    public void ShippedModelsAreInTheTestOutput() =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "models", "v5", "latin_PP-OCRv5_rec_mobile_infer.onnx")));
}
