using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.BubbleCore;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Bubble skins: Core rules (deterministic, capped, inside the glass, still under Off), the soap
/// seam (nothing changes) and offscreen Champagne stills (CCP_SKIN_DUMP=dir writes PNGs).</summary>
public class BubbleSkinsTests
{
    private const string Champ = BubbleSkinIds.Champagne;
    private static readonly double[] Radii = { 8, 27, 60, 140, 400 };

    [Fact]
    public void Choice_is_soap_unless_a_drawn_skin_is_named()
    {
        foreach (var s in new[] { null, "", "glitter", BubbleSkinIds.Heart })
            Assert.Equal(BubbleSkinIds.Soap, BubbleSkinChoice.Resolve(s));
        Assert.Equal(Champ, BubbleSkinChoice.Resolve(" Champagne "));
    }

    [Fact]
    public void Interior_is_deterministic_capped_inside_the_glass_and_still_under_off()
    {
        var a = new SkinPrim[BubbleSkins.MaxInterior];
        var b = new SkinPrim[BubbleSkins.MaxInterior];
        foreach (var r in Radii)
            for (int seed = 0; seed < 40; seed++)
                for (double t = 0; t < 12; t += 0.37)
                {
                    int n = BubbleSkins.Interior(Champ, seed * 7919, t, 1, r, a);
                    Assert.InRange(n, 1, BubbleSkins.MaxInterior);
                    Assert.Equal(n, BubbleSkins.Interior(Champ, seed * 7919, t, 1, r, b));
                    for (int i = 0; i < n; i++)
                    {
                        var p = a[i];
                        Assert.Equal(p, b[i]);
                        double reach = p.Kind == SkinPrimKind.Ring ? p.R * 1.1 : p.R;
                        Assert.True(Math.Sqrt(p.X * p.X + p.Y * p.Y) + reach <= 1.0 + 1e-6, $"r={r} seed={seed} t={t} i={i}");
                        Assert.InRange(p.Alpha, 0f, 1f);
                    }
                }
        Assert.Equal(0, BubbleSkins.Interior(BubbleSkinIds.Soap, 1, 1, 1, 60, a));

        int na = BubbleSkins.Interior(Champ, 5, 0.4, 0, 60, a);   // Off: one frame whatever the time
        BubbleSkins.Interior(Champ, 5, 9.1, 0, 60, b);
        for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
        BubbleSkins.Interior(Champ, 5, 4, .5, 60, a);              // Reduced: half speed
        BubbleSkins.Interior(Champ, 5, 2, 1, 60, b);
        for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void Burst_is_deterministic_capped_rises_and_dies()
    {
        var a = new SkinParticle[BubbleSkins.MaxBurst];
        var b = new SkinParticle[BubbleSkins.MaxBurst];
        foreach (var r in Radii)
        {
            int n = BubbleSkins.Burst(Champ, 42, r, 1, a);
            Assert.InRange(n, 1, BubbleSkins.MaxBurst);
            Assert.Equal(n, BubbleSkins.Burst(Champ, 42, r, 1, b));
            double y0 = 0, y1 = 0;
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(a[i], b[i]);
                Assert.True(Math.Sqrt(a[i].X * a[i].X + a[i].Y * a[i].Y) <= 1.0);
                Assert.False(BubbleSkins.At(a[i], a[i].Life + .01).Alive);
                if (a[i].Kind == SkinParticleKind.Dot && a[i].Life > .9f)
                { y0 += BubbleSkins.At(a[i], 0).Y; y1 += BubbleSkins.At(a[i], .85).Y; }
            }
            Assert.True(y1 < y0, "the foam drifts up");
        }
        Assert.Equal(0, BubbleSkins.Burst(Champ, 42, 60, 0, a));   // Off bursts nothing
        Assert.Equal(0, BubbleSkins.Burst(BubbleSkinIds.Soap, 42, 60, 1, a));
    }

    [Fact]
    public void Soap_and_reserved_skins_leave_the_item_untouched()
    {
        foreach (var skin in new[] { BubbleSkinIds.Soap, null, BubbleSkinIds.Heart, "nope" })
        {
            var fresh = new BubbleLayer.BubbleItem();
            var item = new BubbleLayer.BubbleItem();
            BubbleLayer.ApplySkin(item, skin, 12345);
            foreach (var f in typeof(BubbleLayer.BubbleItem).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.Equal(f.GetValue(fresh), f.GetValue(item));
        }
        var champ = new BubbleLayer.BubbleItem();
        BubbleLayer.ApplySkin(champ, Champ, 12345);
        Assert.Equal(Champ, champ.Skin);
        Assert.Null(BubbleLayer.SkinPainter.ShellTint(BubbleSkinIds.Soap));
    }

    private const int Cell = 300;
    private const float Side = 220f;   // sprite side; the glass is 0.45 of it
    private static readonly SKColor Bg = new(22, 26, 38);

    [Fact]
    public void Champagne_stills_draw_inside_the_glass_and_pop_outside_it()
    {
        using var sprite = StandInSprite();
        using var paint = new BubbleLayer.SkinPainter();
        const int seed = 20261117;
        // Four times, a small bubble, and an Off frame.
        double[] times = { 0, 0.6, 1.3, 2.5 };
        using var strip = Canvas(Cell * 6, out var c);
        for (int i = 0; i < times.Length; i++) Bubble(c, sprite, paint, seed, times[i], 1, i, Side, 1f, 1f);
        Bubble(c, sprite, paint, seed + 1, 1.0, 1, 4, 90f, 1f, 1f);
        Bubble(c, sprite, paint, seed, 7.0, 0, 5, Side, 1f, 1f);
        // Pop: the base pop grows 0.04 and fades 0.066 a step at 30 fps; the burst flies from frame 0.
        var parts = new SkinParticle[BubbleSkins.MaxBurst];
        float glass = Side * 0.45f;
        int n = BubbleSkins.Burst(Champ, seed, glass, 1, parts);
        int[] frames = { 2, 6, 12, 22, 34 };
        using var pop = Canvas(Cell * frames.Length, out var pc);
        for (int i = 0; i < frames.Length; i++)
        {
            int f = frames[i];
            if (1 - .066f * f > 0) Bubble(pc, sprite, paint, seed, 2.5 + f / 30.0, 1, i, Side, 1 + .04f * f, 1 - .066f * f);
            paint.DrawBurst(pc, parts.AsSpan(0, n), f / 30.0, Cell * i + Cell / 2f, Cell / 2f, glass, 1f);
        }
        var dump = Environment.GetEnvironmentVariable("CCP_SKIN_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            Directory.CreateDirectory(dump);
            Save(strip, Path.Combine(dump, "champagne-interior.png"));
            Save(pop, Path.Combine(dump, "champagne-pop.png"));
        }

        Assert.True(Diff(strip, 0, 1) > 200, "the interior moves");
        using var still = Canvas(Cell * 2, out var sc);
        Bubble(sc, sprite, paint, seed, 0.2, 0, 0, Side, 1f, 1f);
        Bubble(sc, sprite, paint, seed, 5.2, 0, 1, Side, 1f, 1f);
        Assert.Equal(0, Diff(still, 0, 1));
        Assert.Equal(Bg, strip.GetPixel(Cell + 8, 8));   // nothing lands outside the glass
        int lit = 0;
        for (int y = 0; y < Cell; y += 2)
            for (int x = Cell * 2; x < Cell * 3; x += 2)
            {
                float dx = x - Cell * 2.5f, dy = y - Cell / 2f;
                if (dx * dx + dy * dy > glass * glass * 1.6f && pop.GetPixel(x, y) != Bg) lit++;
            }
        Assert.True(lit > 20, $"burst pixels outside the glass: {lit}");
    }

    /// <summary>The order and transform of BubbleLayer.Render: tinted sprite, then the interior under its scale.</summary>
    private static void Bubble(SKCanvas c, SKImage sprite, BubbleLayer.SkinPainter paint, int seed, double t, double motion,
                               int cell, float side, float scale, float opacity)
    {
        float cx = Cell * cell + Cell / 2f, cy = Cell / 2f, half = side / 2f;
        int save = c.Save();
        c.Translate(cx, cy); c.Scale(scale, scale); c.Translate(-cx, -cy);
        using var img = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium, Color = SKColors.White.WithAlpha((byte)(opacity * 255)),
                                      ColorFilter = BubbleLayer.SkinPainter.ShellTint(Champ) };
        c.DrawImage(sprite, new SKRect(cx - half, cy - half, cx + half, cy + half), img);
        img.ColorFilter = null;   // the shared filter is app-lifetime: never disposed with this paint
        paint.DrawInterior(c, Champ, seed, t, motion, cx, cy, half * 0.9f, opacity);
        c.RestoreToCount(save);
    }

    /// <summary>A soap stand-in (bubble.png is an LFS pointer here): a rim at 0.45 of the side and a highlight.</summary>
    private static SKImage StandInSprite()
    {
        using var surf = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surf.Canvas;
        c.Clear(SKColors.Transparent);
        using (var sh = SKShader.CreateRadialGradient(new SKPoint(128, 128), 115,
                   new[] { new SKColor(255, 255, 255, 10), new SKColor(220, 230, 255, 40), new SKColor(255, 255, 255, 170) },
                   new[] { 0f, .8f, 1f }, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { IsAntialias = true, Shader = sh })
            c.DrawCircle(128, 128, 115, p);
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 150) })
            c.DrawOval(new SKRect(77, 56, 115, 77), p);
        return surf.Snapshot();
    }

    private static SKBitmap Canvas(int w, out SKCanvas canvas)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, Cell, SKColorType.Rgba8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bmp);
        canvas.Clear(Bg);
        return bmp;
    }

    private static int Diff(SKBitmap bmp, int a, int b)
    {
        int d = 0;
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
                if (bmp.GetPixel(a * Cell + x, y) != bmp.GetPixel(b * Cell + x, y)) d++;
        return d;
    }

    private static void Save(SKBitmap bmp, string path)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(path, data.ToArray());
    }
}
