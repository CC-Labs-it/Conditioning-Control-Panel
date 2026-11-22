using System;
using System.IO;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Shop looks on the Deck (lane J1, frames 2026-11-17): the card frames and their backs (slot <c>card</c>).
/// The default row draws no frame and no back, so the stock card is today's. Set <c>CCP_DECK_DUMP</c> to a
/// folder to write stills.
/// </summary>
public class DeckLooksTests
{
    internal const int CardW = 240, CardH = 300, Cell = 320;
    internal static readonly SKColor Bg = new(0x16, 0x12, 0x1E);

    [Fact]
    public void The_free_row_draws_no_frame() => Assert.False(DeckLooks.FrameShows(DeckLooks.BackOf(WornLooks.DefaultOf(WornLooks.Card))));

    [Theory]
    [InlineData("fx.card.plain", DeckBack.Plain)]
    [InlineData("fx.card.ink", DeckBack.Ink)]
    [InlineData("fx.card.check", DeckBack.Check)]
    [InlineData("fx.card.lace", DeckBack.Lace)]
    [InlineData("fx.card.bogus", DeckBack.Plain)]
    [InlineData("fx.bubble.heart", DeckBack.Plain)]
    [InlineData(null, DeckBack.Plain)]
    public void A_worn_row_names_its_back(string? row, DeckBack back) => Assert.Equal(back, DeckLooks.BackOf(row));

    // ---- card frames (owner 2026-11-17): the same patterns as a border round every Deck card ----

    [Fact]
    public void A_frame_is_a_small_margin_that_scales_with_the_card()
    {
        Assert.False(DeckLooks.FrameShows(DeckBack.Plain));
        Assert.True(DeckLooks.FrameShows(DeckBack.Lace));
        Assert.Equal(0, DeckLooks.FrameBand(30, 200));                       // too small to carry one
        Assert.Equal(DeckLooks.FrameMinPx, DeckLooks.FrameBand(60, 80));
        Assert.Equal(DeckLooks.FrameMaxPx, DeckLooks.FrameBand(2000, 2000));
        double small = DeckLooks.FrameBand(160, 200), big = DeckLooks.FrameBand(480, 600);
        Assert.True(big > small);
        foreach (var (w, h) in new[] { (60.0, 80.0), (160.0, 200.0), (480.0, 600.0), (1200.0, 900.0) })
            Assert.InRange(DeckLooks.FrameBand(w, h) / Math.Min(w, h), 0.02, 0.09);   // never more than a margin
        Assert.True(DeckLooks.FrameInnerRadius(0, 10) > 0);                 // round inside even on a square card
        Assert.Equal(14, DeckLooks.FrameInnerRadius(24, 10));
    }

    [Fact]
    public void Plain_draws_no_frame_and_a_frame_leaves_the_picture_past_its_band()
    {
        using var draw = new DeckLookDraw();
        var r = new SKRect(40, 10, 40 + CardW, 10 + CardH);
        using var plain = Surface(Cell, out var pc);
        Face(pc, r, 320);
        using var before = plain.Snapshot();
        Assert.Equal(0f, draw.DrawFrame(pc, r, 12, DeckBack.Plain, 255));
        using var after = plain.Snapshot();
        Assert.Equal(before.Encode().ToArray(), after.Encode().ToArray());   // byte for byte today's card

        foreach (var back in new[] { DeckBack.Ink, DeckBack.Check, DeckBack.Lace })
        {
            using var s = Surface(Cell, out var c);
            Face(c, r, 320);
            var want = Pixel(s, (int)r.MidX, (int)r.MidY);
            var edgeBefore = Pixel(s, (int)r.Left + 2, (int)r.MidY);
            float band = draw.DrawFrame(c, r, 12, back, 255);
            Assert.Equal((float)DeckLooks.FrameBand(CardW, CardH), band);
            Assert.Equal(want, Pixel(s, (int)r.MidX, (int)r.MidY));                       // the picture stays
            Assert.Equal(Pixel(s, (int)(r.Left + band + 3), (int)r.MidY), PixelOfFace(r, (int)(r.Left + band + 3), (int)r.MidY));
            Assert.NotEqual(edgeBefore, Pixel(s, (int)r.Left + 2, (int)r.MidY));           // the band is there
            Assert.Equal(Bg, Pixel(s, (int)r.Left - 2, (int)r.MidY));                     // nothing past the card
        }
    }

    [Fact]
    public void Stills_of_every_frame_small_and_large()
    {
        using var draw = new DeckLookDraw();
        var backs = new[] { DeckBack.Plain, DeckBack.Ink, DeckBack.Check, DeckBack.Lace };
        float[] hues = { 320, 190, 40, 270 };
        var sizes = new[] { ("small", 120, 150, 140, 8f), ("large", 480, 600, 520, 18f) };
        var dir = Environment.GetEnvironmentVariable("CCP_DECK_DUMP");
        foreach (var (name, w, h, cell, radius) in sizes)
        {
            using var s = Surface(cell * backs.Length, out var c, h + 40);
            for (int i = 0; i < backs.Length; i++)
            {
                var r = new SKRect(cell * i + (cell - w) / 2f, 20, cell * i + (cell + w) / 2f, 20 + h);
                int save = c.Save();
                c.ClipRoundRect(new SKRoundRect(r, radius), antialias: true);   // the layer's rounded card
                Face(c, r, hues[i]);
                draw.DrawFrame(c, r, radius, backs[i], 255);
                c.RestoreToCount(save);
            }
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); Save(s, Path.Combine(dir, $"frames-{name}.png")); }
        }
    }

    private static SKColor PixelOfFace(SKRect r, int x, int y)
    {
        using var s = Surface(Cell, out var c);
        Face(c, r, 320);
        return Pixel(s, x, y);
    }

    internal static SKRect Box(int cell)
        => new(Cell * cell + (Cell - CardW) / 2f, (Cell - CardH) / 2f, Cell * cell + (Cell + CardW) / 2f, (Cell + CardH) / 2f);

    /// <summary>A stand-in picture (the mockup's drawFront): a gradient, a sun and a hill.</summary>
    internal static void Face(SKCanvas c, SKRect r, float hue)
    {
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom),
            new[] { SKColor.FromHsl(hue, 55, 62), SKColor.FromHsl((hue + 40) % 360, 50, 38) }, SKShaderTileMode.Clamp);
        c.DrawRect(r, p);
        p.Shader = null;
        p.Color = new SKColor(255, 255, 255, 140);
        c.DrawCircle(r.Left + r.Width * .72f, r.Top + r.Height * .3f, Math.Min(r.Width, r.Height) * .11f, p);
        using var hill = new SKPath();
        hill.MoveTo(r.Left, r.Bottom); hill.LineTo(r.Left + r.Width * .38f, r.Top + r.Height * .5f);
        hill.LineTo(r.Left + r.Width * .62f, r.Top + r.Height * .78f); hill.LineTo(r.Left + r.Width * .8f, r.Top + r.Height * .6f);
        hill.LineTo(r.Right, r.Bottom); hill.Close();
        p.Color = new SKColor(0x14, 0x10, 0x19, 0x73);
        c.DrawPath(hill, p);
    }

    internal static SKSurface Surface(int w, out SKCanvas c, int h = Cell)
    {
        var s = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        c = s.Canvas;
        c.Clear(Bg);
        return s;
    }

    internal static SKColor Pixel(SKSurface s, int x, int y)
    {
        using var img = s.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        return bmp.GetPixel(x, y);
    }

    internal static void Save(SKSurface s, string path)
    {
        using var img = s.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
