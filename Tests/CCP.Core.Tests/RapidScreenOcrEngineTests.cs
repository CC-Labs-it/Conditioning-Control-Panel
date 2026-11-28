using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using SkiaSharp;
using Xunit;

namespace CCP.Core.Tests;

// Runs on the models CCP.Core ships into this test output (models/v5), so CI exercises the real engine.
public class RapidScreenOcrEngineTests(ITestOutputHelper output)
{
    private static readonly RapidScreenOcrEngine Engine = new();

    private static readonly SKTypeface Face = SKTypeface.FromFile(Fixture()) ?? throw new FileNotFoundException("OCR test font");
    private static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "ocr-test-noto-sans-sc.otf");
    // White 1920x1080 BGRA frame, text drawn black at (x, baseline) in the fixture font (Noto Sans CJK subset to this class's strings, never a runner font).
    private static (ScreenFrame Frame, SKRect Drawn) Frame(string? text, float x = 300, float y = 400)
    {
        var info = new SKImageInfo(1920, 1080, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        using var font = new SKFont(Face, 48);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.Clear(SKColors.White);
        var drawn = SKRect.Empty;
        if (text != null) { canvas.DrawText(text, x, y, font, paint); font.MeasureText(text, out drawn); drawn.Offset(x, y); }
        canvas.Flush();
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
        var words = Engine.Recognize(frame)!;
        output.WriteLine($"{sw.ElapsedMilliseconds} ms: " + string.Join(" | ", words.Select(w => $"{w.Text} {w.Bounds}")));
        Assert.Equal(new[] { "Good", "girl", "obey", "2026" }, words.Select(w => w.Text));
        // DBNet unclips its boxes beyond the glyphs, so allow half an em around the drawn text; the centre must be on it.
        var area = Rectangle.FromLTRB((int)drawn.Left - 24, (int)drawn.Top - 24, (int)drawn.Right + 24, (int)drawn.Bottom + 24);
        Assert.All(words, w => Assert.True(area.Contains(w.Bounds) &&
            drawn.Contains(w.Bounds.X + w.Bounds.Width / 2f, w.Bounds.Y + w.Bounds.Height / 2f), $"{w.Text} {w.Bounds} outside {drawn}"));
        Assert.True(words.Select(w => w.Bounds.Left).SequenceEqual(words.Select(w => w.Bounds.Left).Order()));
    }

    // One engine across the cases, so each one also switches the loaded recognizers to its languages.
    private static string? _language;
    private static readonly RapidScreenOcrEngine ScriptEngine = new(languages: () => ["en", _language]);

    [Theory] // the Latin recognizer reads none of these; an English UI must not stop the OS language's script
    [InlineData("zh_CN.UTF-8", "服从命令")]
    [InlineData("ja-JP", "いい子だね")]
    [InlineData("ko", "착한 소녀")]
    [InlineData("ru", "Хорошая девочка")]
    [InlineData("ja", "Good girl obey")] // two recognizers read the line once
    public void EachLanguageReadsItsScriptBesideAnEnglishUi(string language, string text)
    {
        _language = language;
        var words = ScriptEngine.Recognize(Frame(text).Frame)!;
        output.WriteLine(string.Join(" | ", words.Select(w => $"{w.Text} {w.Bounds}")));
        Assert.Equal(text.Replace(" ", ""), string.Concat(words.Select(w => w.Text)).Replace(" ", ""));
    }

    [Fact]
    public void OsLanguagesPickLatinAndAtMostOneOtherScript()
    {
        var languages = RapidScreenOcrEngine.OsLanguages(n => n switch { "LANGUAGE" => "en_GB:ko:uk", "LANG" => "ja_JP.UTF-8", _ => null }).ToArray();
        Assert.Equal("en_GB|ko|uk|ja_JP.UTF-8", string.Join("|", languages[^5..^1]));
        Assert.Equal(["latin", "korean"], RapidScreenOcrEngine.RecognizerSet(languages).Select(r => r.Rec.Split('_')[0]));
        Assert.Equal(["latin"], RapidScreenOcrEngine.RecognizerSet(["en", "de-DE", null]).Select(r => r.Rec.Split('_')[0]));
    }

    [Fact]
    public void BlankFrameHasNoWords() => Assert.Empty(Engine.Recognize(Frame(null).Frame)!);

    [Fact]
    public void MissingModelsAreUnavailableWithoutThrowing()
    {
        using var engine = new RapidScreenOcrEngine(Path.Combine(Path.GetTempPath(), "ccp-no-ocr-models-" + Guid.NewGuid()));
        Assert.False(engine.IsAvailable);
        Assert.Empty(engine.Recognize(Frame("Good girl").Frame)!);
        Assert.False(engine.IsAvailable);
    }

    [Fact]
    public void ShippedModelsAreInTheTestOutput() =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "models", "v5", "latin_PP-OCRv5_rec_mobile_infer.onnx")));
}
