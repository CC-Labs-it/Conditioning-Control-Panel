using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// What the Deck's spiral backs cost a frame (owner 2026-11-17, "words behind the card": a turning Loom spiral per
/// card plus the phrase coming at you). Offscreen on the CPU raster path the compositor uses, at 2560x1440, with 1, 5,
/// 10 and 20 cards on their backs, every card clipped to its rounded box like FlashLayer draws it. CCP_DECK_BENCH=&lt;dir&gt;
/// runs the full bench and writes the numbers there; without it a short run only proves the path draws and stays
/// under a very generous bound, so a busy CI box never fails it.
/// </summary>
public sealed class DeckSpiralBackBenchTests
{
    private const int W = 2560, H = 1440;

    [Fact]
    public void Spiral_backs_fit_the_frame_budget()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_DECK_BENCH");
        bool full = !string.IsNullOrEmpty(dir);
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var spiral = Spiral();
        using var look = new DeckLookDraw();
        var lines = DeckSpiralBack.Lines("you are so deep now");
        var report = new StringBuilder();
        report.AppendLine($"Deck spiral backs, {W}x{H}, CPU raster, card about 430x600 (portrait) and 600x430, {Environment.ProcessorCount} cores");
        if (full) Directory.CreateDirectory(dir!);
        double tenMs = 0, tenDirect = 0;
        foreach (var (mode, title) in new[] { (0, "Straight at full size (before):"), (1, "Half-size back, one stretch (SpiralBackCache, shipped), turning:"),
                                              (2, "Half-size back, still (motion Off: only the stretch):") })
        {
            report.AppendLine(title);
            foreach (var n in new[] { 1, 5, 10, 20 })
            {
                int frames = full ? 240 : 12;
                var (back, word) = Bench(surface.Canvas, look, spiral, lines, n, frames, cached: mode > 0, still: mode == 2);
                if (n == 10 && mode == 1) tenMs = back + word;
                if (n == 10 && mode == 0) tenDirect = back + word;
                report.AppendLine($"  {n,2} cards: {back + word,6:0.00} ms a frame (spiral + dim {back:0.00}, phrase {word:0.00}); {(back + word) / n:0.00} ms a card");
            }
            if (!full || mode == 2) continue;
            using var shot = surface.Snapshot();
            using var png = shot.Encode(SKEncodedImageFormat.Png, 80);
            using var f = File.Create(Path.Combine(dir!, mode == 0 ? "deck-spiral-20-direct.png" : "deck-spiral-20-cached.png"));
            png.SaveTo(f);
        }
        if (full) File.WriteAllText(Path.Combine(dir!, "deck-spiral-bench.txt"), report.ToString());
        Assert.True(tenMs < 60, report.ToString());
        // The half-size back is the cheaper path wherever it is measured (a loose margin for a busy box).
        if (full) Assert.True(tenMs < tenDirect * 0.8, report.ToString());
    }

    [Fact]
    public void The_half_size_back_matches_the_full_one_and_draws_only_when_it_moves()
    {
        using var spiral = Spiral();
        using var look = new DeckLookDraw();
        var r = new SKRect(20, 30, 450, 630);
        using var a = SKSurface.Create(new SKImageInfo(480, 680, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var b = SKSurface.Create(new SKImageInfo(480, 680, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var cache = new SpiralBackCache();
        foreach (var (s, c) in new[] { (a, (SpiralBackCache?)null), (b, cache) })
        {
            s.Canvas.Clear(SKColors.Black);
            int save = s.Canvas.Save();
            s.Canvas.ClipRect(r);
            look.DrawSpiralBack(s.Canvas, r, spiral, 0xFF2A1240, 0.6, 255, c);
            s.Canvas.RestoreToCount(save);
        }
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        // Same picture, a little softer: the mean difference per channel stays small, and nothing lands off the card.
        long diff = 0, count = 0;
        for (int y = 0; y < 680; y += 3)
            for (int x = 0; x < 480; x += 3)
            {
                var ca = pa.GetPixelColor(x, y); var cb = pb.GetPixelColor(x, y);
                bool inCard = r.Contains(x + 0.5f, y + 0.5f);
                if (!inCard) { Assert.Equal(SKColors.Black, cb); continue; }
                diff += Math.Abs(ca.Red - cb.Red) + Math.Abs(ca.Green - cb.Green) + Math.Abs(ca.Blue - cb.Blue);
                count += 3;
            }
        Assert.InRange(diff / (double)count, 0, 12);
        Assert.Equal(1, cache.Allocations);
        Assert.Equal(1, cache.Composes);
        // Still: stretched again, not drawn again. A turn or a new roll draws it again.
        look.DrawSpiralBack(b.Canvas, r, spiral, 0xFF2A1240, 0.6, 255, cache);
        Assert.Equal(1, cache.Composes);
        look.DrawSpiralBack(b.Canvas, r, spiral, 0xFF2A1240, 0.7, 255, cache);
        Assert.Equal(2, cache.Composes);
        cache.Invalidate();
        look.DrawSpiralBack(b.Canvas, r, spiral, 0xFF2A1240, 0.7, 255, cache);
        Assert.Equal(3, cache.Composes);
        // A card that reshapes a little keeps its bitmap.
        look.DrawSpiralBack(b.Canvas, new SKRect(20, 30, 460, 640), spiral, 0xFF2A1240, 0.7, 255, cache);
        Assert.Equal(1, cache.Allocations);
    }

    /// <summary>Median of the frame times (ms) for the backs and the phrases, n cards, after a warm-up.</summary>
    private static (double Back, double Word) Bench(SKCanvas canvas, DeckLookDraw look, SKBitmap spiral, string[] lines, int n, int frames, bool cached, bool still)
    {
        var caches = Enumerable.Range(0, n).Select(_ => cached ? new SpiralBackCache() : null).ToArray();
        var rects = Enumerable.Range(0, n).Select(i =>
        {
            bool tall = i % 3 != 2;
            float w = tall ? 430 : 600, h = tall ? 600 : 430;
            float x = 60 + (i * 487) % (W - 700), y = 60 + (i / 5) * 160 % (H - 650);
            return new SKRect(x, y, x + w, y + h);
        }).ToArray();
        var backs = new double[frames];
        var words = new double[frames];
        for (int f = -4; f < frames; f++)
        {
            canvas.Clear(SKColors.Transparent);
            double turn = still ? 0.3 : Math.Max(0, f) * 0.02;
            long a = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++)
            {
                int save = canvas.Save();
                using (var rr = new SKRoundRect(rects[i], 14)) canvas.ClipRoundRect(rr, antialias: true);
                look.DrawSpiralBack(canvas, rects[i], spiral, 0xFF2A1240, turn + i, 255, caches[i]);
                canvas.RestoreToCount(save);
            }
            long b = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++)
            {
                int save = canvas.Save();
                using (var rr = new SKRoundRect(rects[i], 14)) canvas.ClipRoundRect(rr, antialias: true);
                // Mid-zoom, ghosts on: the busiest moment of the phrase.
                var p = DeckSpiralBack.Word(0.9 + (f % 10) * 0.01, 1, motionOff: false, gentle: false);
                look.DrawBackWord(canvas, rects[i], lines, p, 300, 255);
                canvas.RestoreToCount(save);
            }
            canvas.Flush();
            long c = Stopwatch.GetTimestamp();
            if (f < 0) continue;
            backs[f] = (b - a) * 1000.0 / Stopwatch.Frequency;
            words[f] = (c - b) * 1000.0 / Stopwatch.Frequency;
        }
        foreach (var c in caches) c?.Dispose();
        Array.Sort(backs); Array.Sort(words);
        return (backs[frames / 2], words[frames / 2]);
    }

    /// <summary>A stand-in for a rolled Loom spiral: the same size and pixel format (FlashLayer.BackSpiralPx, Bgra8888).</summary>
    private static SKBitmap Spiral()
    {
        const int px = 320;
        var bmp = new SKBitmap(new SKImageInfo(px, px, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(0x2A, 0x12, 0x40));
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 18, Color = new SKColor(0xFF, 0x69, 0xB4) };
        using var path = new SKPath();
        for (double t = 0; t < 40; t += 0.05)
        {
            float r = (float)(t * 4), x = (float)(px / 2 + Math.Cos(t) * r), y = (float)(px / 2 + Math.Sin(t) * r);
            if (t == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        c.DrawPath(path, paint);
        return bmp;
    }
}
