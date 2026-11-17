using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.BubbleCore;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The small pictures on the Studio look chips (<see cref="LookRow"/>): a bubble skin is one still frame of the real
/// skin painter (the same code the screen draws), rendered once and cached; a card frame is its ground and mark
/// colours; a word style is "Aa" in the style's face. All frozen, made on first use only.
/// </summary>
internal static class LookThumbs
{
    public const int Px = 44;
    private static readonly Dictionary<string, ImageSource?> s_bubbles = new(StringComparer.Ordinal);

    /// <summary>A still of one bubble skin, <see cref="Px"/> square. Null when Skia is unavailable.</summary>
    public static ImageSource? Bubble(string skin)
    {
        if (s_bubbles.TryGetValue(skin, out var hit)) return hit;
        ImageSource? made = null;
        try { made = RenderBubble(skin); }
        catch (Exception ex) { App.Logger?.Debug("LookThumbs: bubble {Skin} failed: {E}", skin, ex.Message); }
        s_bubbles[skin] = made;
        return made;
    }

    private static ImageSource RenderBubble(string skin)
    {
        const int seed = 20261117;
        using var bmp = new SKBitmap(new SKImageInfo(Px, Px, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            float cx = Px / 2f, cy = Px / 2f, half = Px / 2f - 1, glass = half * BubbleLayer.GlassOfHalf;
            using var art = skin == BubbleSkinIds.Spiral ? BubbleLayer.SkinPainter.SpiralArt(seed, glass) : null;
            using var paint = new BubbleLayer.SkinPainter();
            bool drawn = BubbleSkinIds.IsDrawn(skin);
            if (drawn && BubbleSkins.UnderShell(skin)) paint.DrawInterior(c, skin, seed, 1.3, 1, cx, cy, glass, 1f, art);
            if (!drawn || !BubbleSkins.OwnsBody(skin)) Shell(c, cx, cy, glass, skin);
            if (drawn && !BubbleSkins.UnderShell(skin)) paint.DrawInterior(c, skin, seed, 1.3, 1, cx, cy, glass, 1f, null);
        }
        var src = BitmapSource.Create(Px, Px, 96, 96, PixelFormats.Pbgra32, null, bmp.Bytes, bmp.RowBytes);
        src.Freeze();
        return src;
    }

    /// <summary>A soap shell: a faint fill, a bright rim and a highlight (the shipped sprite may be an LFS pointer here).</summary>
    private static void Shell(SKCanvas c, float cx, float cy, float r, string skin)
    {
        var tint = skin == BubbleSkinIds.Champagne ? new SKColor(255, 212, 140) : skin == BubbleSkinIds.Frost ? new SKColor(190, 225, 255) : new SKColor(220, 230, 255);
        using var fill = new SKPaint { IsAntialias = true };
        using (fill.Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r,
                   new[] { tint.WithAlpha(14), tint.WithAlpha(46), SKColors.White.WithAlpha(180) }, new[] { 0f, .82f, 1f }, SKShaderTileMode.Clamp))
            c.DrawCircle(cx, cy, r, fill);
        fill.Shader = null;
        fill.Color = SKColors.White.WithAlpha(150);
        c.DrawOval(cx - r * .38f, cy - r * .42f, r * .2f, r * .12f, fill);
    }

    /// <summary>A card frame chip: the card's band in the frame's colours (Plain = a bare card edge).</summary>
    public static Brush CardGround(SuperCore.DeckBack back) => Frozen(SuperCore.DeckLooks.Ground(back));
    public static Brush CardMark(SuperCore.DeckBack back) => Frozen(SuperCore.DeckLooks.Mark(back));

    /// <summary>The Plain chip, so it reads as "no frame" rather than a pink card: a dim picture card with a thin neutral
    /// edge and a small "none" mark (a ring with a slash) over it.</summary>
    public static FrameworkElement PlainCard(double w, double h)
    {
        var sky = new LinearGradientBrush(Color.FromRgb(0x6A, 0x86, 0xB0), Color.FromRgb(0x2E, 0x3A, 0x55), 90);
        sky.Freeze();
        var card = new System.Windows.Controls.Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(3),
            Background = sky, BorderBrush = Frozen(0x99FFFFFF), BorderThickness = new Thickness(1),
        };
        double d = Math.Min(w, h) * 0.75, r = d / 2 - 0.9, k = r * 0.7071;
        var mark = new GeometryGroup();
        mark.Children.Add(new EllipseGeometry(new Point(d / 2, d / 2), r, r));
        mark.Children.Add(new LineGeometry(new Point(d / 2 - k, d / 2 + k), new Point(d / 2 + k, d / 2 - k)));
        mark.Freeze();
        var none = new System.Windows.Shapes.Path
        {
            Data = mark, Width = d, Height = d, Stroke = Frozen(0xF2FFFFFF), StrokeThickness = 1.6,
            Fill = Frozen(0x661D0C26), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var g = new System.Windows.Controls.Grid { Width = w, Height = h };
        g.Children.Add(card);
        g.Children.Add(none);
        return g;
    }

    /// <summary>The font of a word style's "Aa", with the face's own fallbacks.</summary>
    public static FontFamily WordFont(global::ConditioningControlPanel.Looks.WordStyle style)
    {
        var f = global::ConditioningControlPanel.Looks.WordStyles.FaceOf(style).Families;
        return new FontFamily(f == null ? "Segoe UI" : string.Join(", ", f) + ", Segoe UI");
    }

    public static Brush? WordInk(global::ConditioningControlPanel.Looks.WordStyle style)
        => global::ConditioningControlPanel.Looks.WordStyles.FaceOf(style).Colour is uint c ? Frozen(0xFF000000 | c) : null;

    public static Brush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
}
