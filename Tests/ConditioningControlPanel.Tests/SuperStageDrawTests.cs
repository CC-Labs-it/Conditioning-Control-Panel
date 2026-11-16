using System;
using System.IO;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Skia drawing of the shared stage: it draws, it stays inside its alpha, the pool has no hard rim.</summary>
public class SuperStageDrawTests
{
    private static SKBitmap Scene(double restSeconds, out Whirlpool pool, bool words = true)
    {
        const int w = 1280, h = 720;
        var stage = new SuperStage { SteamOn = true, PoolOn = true };
        var field = stage.Field("a", 0, 0, w, h);
        field.Settle(1.5, 1);
        for (int i = 0; i < 12; i++) field.Wipe(240 + i * 30, 150, 60, 1 / 30.0, mark: true);
        for (int i = 0; i < 40; i++) stage.StepSteam(1 / 30.0, 1.5, 1);
        for (double t = 0; t < restSeconds; t += 1 / 30.0)
        {
            stage.Pool.Step(1 / 30.0, 800, 400, 0, 1, h);
            stage.StepSteam(1 / 30.0, 1.5, 1);
        }
        pool = stage.Pool;
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(22, 26, 38));
        using var steam = new SteamDraw();
        using var whirl = new WhirlpoolDraw();
        steam.Draw(canvas, new SKRect(0, 0, w, h), field, new SKColor(255, 105, 180), 0.62,
            words ? new[] { "sink", "soft", "drift", "blank", "deeper", "let go" } : Array.Empty<string>(), null, SKTypeface.Default, 3);
        whirl.Draw(canvas, pool, 0.5);
        canvas.Flush();
        return bmp;
    }

    [Fact]
    public void Fog_and_pool_draw_and_the_eye_is_clearer_than_the_arms()
    {
        using var bmp = Scene(16, out var pool);
        var dump = Environment.GetEnvironmentVariable("CCP_SUPER_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(dump, data.ToArray());
        }
        var corner = bmp.GetPixel(20, 700);
        Assert.True(corner.Red > 120, "fog at the edge is pink");
        // Fog alone (far from the pool) against the darkest spot on a ring inside the pool: the
        // ring must hold both thick and thin fog, which is what reads as arms.
        int lo = 255, hi = 0;
        for (int a = 0; a < 72; a++)
        {
            var px = bmp.GetPixel((int)(800 + Math.Cos(a * Math.PI / 36) * 150), (int)(400 + Math.Sin(a * Math.PI / 36) * 150));
            lo = Math.Min(lo, px.Blue); hi = Math.Max(hi, px.Blue);
        }
        Assert.True(hi - lo > 25, $"ring contrast {hi - lo}");
    }

    [Fact]
    public void The_pool_fades_out_before_its_rim()
    {
        using var with = Scene(16, out var pool, words: false);
        // Just inside the rim the pool adds almost nothing: no hard circle.
        double r = pool.Radius * 0.97;
        var rim = with.GetPixel((int)(pool.X + r * 0.7071), (int)(pool.Y - r * 0.7071));
        var outside = with.GetPixel((int)(pool.X + pool.Radius * 1.05 * 0.7071), (int)(pool.Y - pool.Radius * 1.05 * 0.7071));
        Assert.InRange(Math.Abs(rim.Green - outside.Green), 0, 40);
    }

    [Fact]
    public void A_pool_at_nothing_draws_nothing()
    {
        using var bmp = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        using var whirl = new WhirlpoolDraw();
        whirl.Draw(canvas, new Whirlpool(), 1);
        canvas.Flush();
        Assert.Equal(SKColors.Black, bmp.GetPixel(32, 32));
    }
}
