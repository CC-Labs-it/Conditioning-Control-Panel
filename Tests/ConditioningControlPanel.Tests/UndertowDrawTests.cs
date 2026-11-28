using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Undertow picture, now Core's <see cref="UndertowDraw"/> (moved out of WPF BrainDrainLayer so
/// the Avalonia head draws the same lens). Runs on WPF's SkiaSharp 2.88.
/// </summary>
public class UndertowDrawTests
{
    private static UndertowState Lens(double x, double y, double w)
    {
        var s = new UndertowState();
        for (int i = 0; i < 60; i++) UndertowLens.Step(s, 1 / 30.0, x, y, w, UndertowMotion.Full);
        return s;
    }

    [Fact]
    public void The_lens_is_cut_through_the_blur_and_the_rest_stays()
    {
        var s = Lens(400, 300, 800);
        using var src = SKSurface.Create(new SKImageInfo(200, 150, SKColorType.Bgra8888, SKAlphaType.Premul));
        src.Canvas.Clear(new SKColor(0x80, 0x80, 0x80));
        using var frame = src.Snapshot();
        using var target = SKSurface.Create(new SKImageInfo(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul));
        target.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, Color = SKColors.White };
        using var draw = new UndertowDraw();
        draw.Draw(target.Canvas, s, frame, new SKRect(0, 0, 800, 600), paint, 800, UndertowMotion.Full, 1);
        using var img = target.Snapshot();
        using var px = SKBitmap.FromImage(img);
        Assert.Equal(0, px.GetPixel(400, 300).Alpha);
        Assert.Equal(255, px.GetPixel(5, 5).Alpha);
    }

    [Fact]
    public void A_click_fills_one_wave_and_a_quiet_lens_none()
    {
        var s = Lens(400, 300, 800);
        float[] w = new float[24], k = new float[24], p = new float[3];
        Assert.False(UndertowDraw.FillWaves(s, 800, UndertowMotion.Full, 1, w, k, p));
        UndertowLens.Click(s, 400, 300, 800, UndertowMotion.Full);
        UndertowLens.Step(s, 0.1, 400, 300, 800, UndertowMotion.Full);
        Assert.True(UndertowDraw.FillWaves(s, 800, UndertowMotion.Full, 1, w, k, p));
        Assert.Equal(400f, w[0]);
        Assert.True(w[3] > 0 && p[0] > 0);
        Assert.False(UndertowDraw.FillWaves(s, 800, UndertowMotion.Off, 1, w, k, p));
    }

    /// <summary>Why WPF ships without click waves: SkiaSharp 2.88 refuses the shader's src.eval
    /// (AGENTS.md SKIA STRETCH TRAP; owner call owed). When this starts compiling, the waves draw.</summary>
    [Fact]
    public void WPF_skia_refuses_the_wave_shader()
    {
        using var fx = SKRuntimeEffect.Create(UndertowDraw.WaveSksl, out var err);
        Assert.Null(fx);
        Assert.Contains("eval", err);
    }
}
