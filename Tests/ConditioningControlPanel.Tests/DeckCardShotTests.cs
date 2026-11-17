using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Deck drawn through the REAL flash layer, offscreen (owner notes 2026-11-17: the card fits its
/// picture, a smoother turn, the spiral back with words coming at you). The layer is built on an engine
/// that was never started (Wake does nothing without a dispatcher), stepped with Update and drawn with
/// Render exactly as the compositor does. Set <c>CCP_DECK_DUMP</c> to a folder to write the stills.
/// </summary>
[Collection("LookWardrobe")]   // sets WornLooks.Source: never in parallel with the wardrobe tests
public class DeckCardShotTests
{
    private const int W = 760, H = 560;
    private static readonly SKColor Desk = new(0x1D, 0x22, 0x30);
    private static readonly Dictionary<string, bool> Pool = new() { ["good girls obey"] = true, ["Sink"] = true, ["deeper and deeper for me"] = true };

    /// <summary>A layer on an engine that was never built and reads as disposed, so Wake returns at once even
    /// when another test has a WPF Application up (it would post the wake to that dispatcher otherwise).</summary>
    private static FlashLayer NewLayer()
    {
        var engine = (CompositorEngine)RuntimeHelpers.GetUninitializedObject(typeof(CompositorEngine));
        typeof(CompositorEngine).GetField("_disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(engine, true);
        return new(engine);
    }

    /// <summary>A stand-in picture: a gradient with a ring and a border, so crops and bars both show.</summary>
    private static SKImage Picture(int w, int h, float hue)
    {
        using var s = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = s.Canvas;
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
            new[] { SKColor.FromHsl(hue, 60, 66), SKColor.FromHsl((hue + 50) % 360, 55, 34) }, SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, w, h, p);
        p.Shader = null;
        p.Style = SKPaintStyle.Stroke;
        p.StrokeWidth = Math.Max(2, Math.Min(w, h) * 0.04f);
        p.Color = new SKColor(255, 255, 255, 200);
        c.DrawCircle(w / 2f, h * 0.42f, Math.Min(w, h) * 0.28f, p);
        p.Color = new SKColor(255, 240, 120, 230);
        c.DrawRect(p.StrokeWidth / 2, p.StrokeWidth / 2, w - p.StrokeWidth, h - p.StrokeWidth, p);
        p.Style = SKPaintStyle.Fill;
        p.Color = new SKColor(0x10, 0x0c, 0x18, 120);
        c.DrawRect(0, h * 0.78f, w, h * 0.22f, p);
        return s.Snapshot();
    }

    private static FlashLayer.FlashItem Card(FlashLayer layer, SKImage face, float cx, float cy, float w, float h)
    {
        var item = layer.Spawn(new[] { face }, cx - w / 2, cy - h / 2, w, h, 0, 14, SKColors.HotPink, 0, 0, false);
        item.Opacity = 1;
        item.Deck = FlickerDeck.Create(0, new Random(4), 9, 9);   // far from breaking: no cracks in the stills
        item.DeckBounds = new SKRect(0, 0, W, H);
        return item;
    }

    private static void Wear(string? card, string? behind)
    {
        WornLooks.Source = slot => slot == WornLooks.Card ? card : slot == WornLooks.Behind ? behind : null;
        WornLooks.Raise(WornLooks.Card);
        WornLooks.Raise(WornLooks.Behind);
    }

    private static void Unwear()
    {
        WornLooks.Source = null;
        WornLooks.Raise(WornLooks.Card);
        WornLooks.Raise(WornLooks.Behind);
    }

    /// <summary>Step the layer in small ticks up to <paramref name="to"/> seconds; returns the new clock.</summary>
    private static double Run(FlashLayer layer, double from, double to)
    {
        const double dt = 1 / 240.0;
        while (from + 1e-9 < to)
        {
            var step = Math.Min(dt, to - from);
            layer.Update(TimeSpan.FromSeconds(step));
            from += step;
        }
        return from;
    }

    private static SKImage Shot(FlashLayer layer, SKRectI crop)
    {
        using var s = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        s.Canvas.Clear(Desk);
        layer.Render(s.Canvas, new SKRectI(0, 0, W, H), 1.0, TimeSpan.Zero);
        using var full = s.Snapshot();
        return full.Subset(crop);
    }

    private static SKSurface Strip(IReadOnlyList<SKImage> frames, int gap = 6)
    {
        int w = 0, h = 0;
        foreach (var f in frames) { w += f.Width + gap; h = Math.Max(h, f.Height); }
        var s = SKSurface.Create(new SKImageInfo(Math.Max(1, w - gap), h, SKColorType.Rgba8888, SKAlphaType.Premul));
        s.Canvas.Clear(new SKColor(0x0E, 0x10, 0x18));
        int x = 0;
        foreach (var f in frames) { s.Canvas.DrawImage(f, x, 0); x += f.Width + gap; }
        return s;
    }

    private static void Dump(SKSurface s, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_DECK_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        DeckLooksTests.Save(s, Path.Combine(dir, name));
    }

    private static SKColor At(SKImage img, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(img);
        return bmp.GetPixel(x, y);
    }

    [Fact]
    public void A_tall_picture_flipped_onto_a_wide_card_turns_into_a_tall_card_with_no_side_bars()
    {
        try
        {
            Wear(null, null);
            var layer = NewLayer();
            var item = Card(layer, Picture(320, 180, 200), W / 2f, H / 2f, 480, 270);
            var frames = new List<SKImage>();
            var crop = new SKRectI(80, 0, W - 80, H);
            frames.Add(Shot(layer, crop));
            FlickerDeck.Press(item.Deck!, MotionLevel.Full);
            item.DeckPendingFrames = new[] { Picture(180, 320, 320) };
            double t = 0;
            foreach (var at in new[] { 0.2, 0.3, 0.37, 0.4, 0.45, 0.5, 0.56, 0.9 })
            {
                t = Run(layer, t, at);
                frames.Add(Shot(layer, crop));
            }
            using var strip = Strip(frames);
            Dump(strip, "flip-wide-to-tall.png");

            // The card is the picture's shape now: tall, same area, same centre.
            Assert.True(item.H > item.W * 1.6);
            Assert.Equal(16.0 / 9, item.H / item.W, 2);
            Assert.InRange(item.W * item.H, 480 * 270 * 0.99, 480 * 270 * 1.01);
            Assert.Null(item.DeckShapeFrom);
            // Settled: a pixel just inside the side of the card is the picture, not black and not the desk.
            var last = frames[^1];
            var side = At(last, (int)(item.X - crop.Left + 10), (int)(item.Y + item.H / 2));
            Assert.NotEqual(SKColors.Black, side);
            Assert.NotEqual(Desk, side);
            // Just past the card's side: the desk (no wide black box left behind).
            Assert.Equal(Desk, At(last, (int)(item.X - crop.Left - 30), (int)(item.Y + item.H / 2)));
        }
        finally { Unwear(); }
    }

    [Fact]
    public void Stills_of_tall_and_wide_cards_and_every_frame_on_a_tall_card()
    {
        try
        {
            var shots = new List<SKImage>();
            foreach (var (row, hue) in new[] { ((string?)null, 320f), ("fx.card.ink", 200f), ("fx.card.check", 40f), ("fx.card.lace", 270f) })
            {
                Wear(row, null);
                var layer = NewLayer();
                Card(layer, Picture(180, 320, hue), W / 2f, H / 2f, 243, 432);
                layer.Update(TimeSpan.FromMilliseconds(16));
                shots.Add(Shot(layer, new SKRectI(W / 2 - 140, H / 2 - 236, W / 2 + 140, H / 2 + 236)));
            }
            using var frames = Strip(shots);
            Dump(frames, "frames-tall.png");

            Wear("fx.card.ink", null);
            var both = NewLayer();
            Card(both, Picture(180, 320, 320), 170, H / 2f, 243, 432);
            Card(both, Picture(320, 180, 200), 520, H / 2f, 400, 225);
            both.Update(TimeSpan.FromMilliseconds(16));
            using var pair = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
            pair.Canvas.Clear(Desk);
            both.Render(pair.Canvas, new SKRectI(0, 0, W, H), 1.0, TimeSpan.Zero);
            Dump(pair, "cards-tall-and-wide.png");
            Assert.NotEqual(Desk, DeckLooksTests.Pixel(pair, 170, H / 2));
        }
        finally { Unwear(); }
    }

    [Fact]
    public void With_words_behind_the_card_the_flip_turns_to_a_spiral_back_with_the_phrase_coming_at_you()
    {
        try
        {
            Wear("fx.card.lace", "fx.behind.whisper");
            var layer = NewLayer();
            var item = Card(layer, Picture(180, 320, 330), W / 2f, H / 2f + 20, 240, 426);
            var crop = new SKRectI(W / 2 - 170, 0, W / 2 + 170, H);
            var flip = new List<SKImage> { Shot(layer, crop) };
            FlickerDeck.Press(item.Deck!, MotionLevel.Full);
            layer.RollBack(item, Pool, 0xFF69B4);
            item.DeckBackNext = true;
            double t = 0;
            foreach (var at in new[] { 0.12, 0.22, 0.3, 0.36, 0.385, 0.42, 0.47, 0.53, 0.62 })
            {
                t = Run(layer, t, at);
                flip.Add(Shot(layer, crop));
            }
            Assert.True(item.DeckOnBack);
            Assert.NotNull(item.WhisperLines);
            using (var strip = Strip(flip)) Dump(strip, "flip-to-spiral-back.png");

            // The phrase coming at you: six beats after the back lands, on a fresh card (the clock above is past them).
            var words = new List<SKImage>();
            var wl = NewLayer();
            var wc = Card(wl, Picture(180, 320, 330), W / 2f, H / 2f, 240, 426);
            FlickerDeck.Press(wc.Deck!, MotionLevel.Full);
            wl.RollBack(wc, Pool, 0xFF69B4);
            wc.DeckBackNext = true;
            double wt = 0;
            while (!wc.DeckOnBack) wt = Run(wl, wt, wt + 1 / 240.0);
            double swapAt = wt;
            foreach (var age in new[] { 0.04, 0.2, 0.42, 0.95, 1.4, 1.6 })
            {
                wt = Run(wl, wt, swapAt + age);
                words.Add(Shot(wl, crop));
            }
            using (var strip = Strip(words)) Dump(strip, "words-approach.png");
            var firstPhrase = item.WhisperText;
            var firstSpec = item.DeckSpiralSpec;

            // The next flip shows a picture again, the one after that a new spiral and a new phrase.
            FlickerDeck.Press(item.Deck!, MotionLevel.Full);
            Assert.False(layer.PrepareDeckBack(item));   // on the back: this flip fetches a picture
            item.DeckBackNext = false;
            item.DeckPendingFrames = new[] { Picture(320, 180, 150) };
            t = Run(layer, t, t + 1.0);
            Assert.False(item.DeckOnBack);
            Assert.True(item.W > item.H);                        // the new picture's shape
            using (var back = Strip(new[] { Shot(layer, crop) })) Dump(back, "back-to-picture.png");
            FlickerDeck.Press(item.Deck!, MotionLevel.Full);
            layer.RollBack(item, Pool, 0xFF69B4);
            item.DeckBackNext = true;
            t = Run(layer, t, t + 1.0);
            Assert.True(item.DeckOnBack);
            Assert.NotEqual(firstPhrase, item.WhisperText);
            Assert.False(DeckSpiralBack.SameShape(firstSpec!, item.DeckSpiralSpec!));
        }
        finally { Unwear(); }
    }

    [Fact]
    public void Without_the_look_a_press_fetches_a_picture_as_before()
    {
        try
        {
            Wear(null, null);
            var layer = NewLayer();
            var item = Card(layer, Picture(180, 320, 330), W / 2f, H / 2f, 270, 480);
            FlickerDeck.Press(item.Deck!, MotionLevel.Full);
            Assert.False(layer.PrepareDeckBack(item));
            Assert.False(item.DeckBackNext);
        }
        finally { Unwear(); }
    }
}
