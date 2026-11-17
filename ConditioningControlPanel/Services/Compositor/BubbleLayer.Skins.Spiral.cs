using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>The Spiral skin's draw: the bubble's own Loom spiral, baked once into a small picture
/// (<see cref="SkinArt"/>) and turned under the soap film; the wind-in of its pop with two trailing echoes.
/// The picture belongs to the bubble, then to its burst, and is freed when either goes.</summary>
public sealed partial class BubbleLayer
{
    /// <summary>A skin's per-bubble pictures (the Spiral's one or two weaves) and their turn directions.</summary>
    internal sealed class SkinArt : IDisposable
    {
        public SKImage? A, B;
        public int Dir1 = 1, Dir2 = -1;

        public void Dispose()
        {
            A?.Dispose(); A = null;
            B?.Dispose(); B = null;
        }
    }

    public sealed partial class BubbleItem
    {
        /// <summary>The skin's baked pictures while the bubble lives (handed to its burst on a pop).</summary>
        internal SkinArt? SkinArt;

        internal void ReleaseSkinArt()
        {
            SkinArt?.Dispose();
            SkinArt = null;
        }
    }

    internal sealed partial class SkinPainter
    {
        private static SKPaint? s_artPaint;
        private static SKPaint ArtPaint() => s_artPaint ??= new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };

        /// <summary>Bake this bubble's spiral for a glass of <paramref name="r"/> pixels: about one texel per pixel, at most 128.</summary>
        public static SkinArt SpiralArt(int seed, float r)
        {
            var spec = BubbleSkins.SpiralSpec(seed);
            int size = BubbleSkins.SpiralArtSize(r);
            var buf = new byte[size * size * 4];
            var art = new SkinArt { Dir1 = spec.Layer.Direction, Dir2 = spec.Layer2.Direction };
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            BubbleSkins.SpiralBake(spec, false, size, buf);
            art.A = SKImage.FromPixelCopy(info, buf, size * 4);
            if (BubbleSkins.SpiralBake(spec, true, size, buf)) art.B = SKImage.FromPixelCopy(info, buf, size * 4);
            return art;
        }

        /// <summary>The spiral at glass radius <paramref name="r"/>. Alive (k = 0) it is drawn under the layer's sprite;
        /// dying (k &gt; 0) it winds in with two echoes and draws the fading soap film itself, then a white point at
        /// the very end (never under Gentle).</summary>
        private void DrawSpiral(SKCanvas c, int seed, double seconds, double motion, float cx, float cy, float r, float alpha,
                                float k, bool gentle, SkinArt? art, SKImage? sprite)
        {
            if (art?.A == null) return;
            if (k > 0)
            {
                SpiralOnce(c, art, seed, seconds, motion, k, cx, cy, r, (float)BubbleSkins.SpiralEchoScale(k, .14), .25f * alpha, .5);
                SpiralOnce(c, art, seed, seconds, motion, k, cx, cy, r, (float)BubbleSkins.SpiralEchoScale(k, .07), .4f * alpha, .25);
            }
            float sc = (float)BubbleSkins.SpiralScale(seed, seconds, motion, k);
            SpiralOnce(c, art, seed, seconds, motion, k, cx, cy, r, sc, alpha, 0);
            if (k <= 0) return;
            if (sprite != null)
            {
                float half = r / GlassOfHalf * sc;
                var paint = ArtPaint();
                paint.Color = SKColors.White.WithAlpha(A((1 - k * .7f) * alpha));
                c.DrawImage(sprite, new SKRect(cx - half, cy - half, cx + half, cy + half), paint);
                paint.Color = SKColors.White;
            }
            if (k > .8f && !gentle)
            {
                _fill.Color = SKColors.White.WithAlpha(A(Math.Min(1f, (k - .8f) * 5) * alpha));
                c.DrawCircle(cx, cy, r * .12f, _fill);
            }
        }

        private static void SpiralOnce(SKCanvas c, SkinArt art, int seed, double seconds, double motion, float k,
                                       float cx, float cy, float r, float scale, float alpha, double lag)
        {
            if (alpha <= .004f || scale <= .005f) return;
            float ri = r * (float)BubbleSkins.SpiralReach * scale;
            var dst = new SKRect(-ri, -ri, ri, ri);
            var paint = ArtPaint();
            int save = c.Save();
            c.Translate(cx, cy);
            if (art.B != null)
            {
                int s2 = c.Save();
                c.RotateRadians((float)(art.Dir2 * BubbleSkins.SpiralTurn(seed, seconds, motion, k, lag, true)));
                paint.Color = SKColors.White.WithAlpha(A(.8f * alpha));
                c.DrawImage(art.B, dst, paint);
                c.RestoreToCount(s2);
            }
            c.RotateRadians((float)(art.Dir1 * BubbleSkins.SpiralTurn(seed, seconds, motion, k, lag, false)));
            paint.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawImage(art.A, dst, paint);
            paint.Color = SKColors.White;
            c.RestoreToCount(save);
        }
    }
}
