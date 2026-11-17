using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>The Frost skin's draw: under the ice-tinted film a pale tint, rime specks and the growing snowflake;
/// the cracks of its pre-burst (with the film, which the burst then draws); its puff, sticks and flakes.
/// Shapes and timing come from <see cref="BubbleSkins"/>; the filter and shader are built once, app-lifetime.</summary>
public sealed partial class BubbleLayer
{
    internal sealed partial class SkinPainter
    {
        private static SKColorFilter? s_iceShell;
        private static SKShader? s_frostPuff;

        private readonly float[] _frostSeg = new float[BubbleSkins.MaxFrostSegments * 4];

        private static SKColorFilter IceShell() => s_iceShell ??= SKColorFilter.CreateBlendMode(new SKColor(160, 214, 255, 178), SKBlendMode.SrcATop);

        private static SKShader FrostPuff() => s_frostPuff ??= SKShader.CreateRadialGradient(new SKPoint(0, 0), 1,
            new[] { new SKColor(200, 232, 255, 204), new SKColor(255, 255, 255, 0) }, SKShaderTileMode.Clamp);

        /// <summary>The frost inside glass radius <paramref name="r"/>. <paramref name="k"/> &gt; 0 is the pre-burst: the
        /// ice film (<paramref name="sprite"/>) is drawn here too and cracks run out from the middle.</summary>
        private void DrawFrost(SKCanvas c, int seed, double seconds, double motion, float cx, float cy, float r, float alpha, float k, SKImage? sprite)
        {
            float g = (float)BubbleSkins.FrostGrowth(seconds, motion);
            int save = c.Save();
            c.Translate(cx, cy);
            c.Scale(r, r);
            _fill.Color = new SKColor(190, 228, 255, A((.08f + .16f * g) * alpha));
            c.DrawCircle(0, 0, (float)BubbleSkins.Inner, _fill);
            int n = BubbleSkins.Interior(BubbleSkinIds.Frost, seed, seconds, motion, r, _prims);
            for (int i = 0; i < n; i++)
            {
                var p = _prims[i];
                _fill.Color = new SKColor(240, 250, 255, A(p.Alpha * alpha));
                c.DrawRect(p.X - p.R, p.Y - p.R, p.R * 2, p.R * 2, _fill);
            }
            int segs = BubbleSkins.FrostFlake(seed, seconds, motion, _frostSeg);
            _poly.Rewind();
            for (int i = 0; i < segs; i++)
            {
                _poly.MoveTo(_frostSeg[i * 4], _frostSeg[i * 4 + 1]);
                _poly.LineTo(_frostSeg[i * 4 + 2], _frostSeg[i * 4 + 3]);
            }
            _stroke.StrokeCap = SKStrokeCap.Round;
            _stroke.Color = new SKColor(236, 248, 255, A(.94f * alpha));
            _stroke.StrokeWidth = Math.Max(1f / r, .034f);
            c.DrawPath(_poly, _stroke);
            c.RestoreToCount(save);

            if (k <= 0) { _stroke.StrokeCap = SKStrokeCap.Butt; return; }
            if (sprite != null)
            {
                float half = r / GlassOfHalf;
                var paint = ArtPaint();
                paint.Color = SKColors.White.WithAlpha(A(alpha));
                paint.ColorFilter = IceShell();
                c.DrawImage(sprite, new SKRect(cx - half, cy - half, cx + half, cy + half), paint);
                paint.ColorFilter = null;   // the shared filter is app-lifetime
                paint.Color = SKColors.White;
            }
            // Five cracks run out from the middle (the pre-burst is 0.3 s under Gentle, so they brighten slowly there).
            _poly.Rewind();
            for (int i = 0; i < BubbleSkins.FrostCracks; i++)
            {
                double an = BubbleSkins.FrostCrack(seed, i);
                _poly.MoveTo(cx, cy);
                _poly.LineTo(cx + (float)Math.Cos(an) * r * .9f * k, cy + (float)Math.Sin(an) * r * .9f * k);
            }
            _stroke.Color = SKColors.White.WithAlpha(A(k * alpha));
            _stroke.StrokeWidth = Math.Max(1f, r * .03f);
            c.DrawPath(_poly, _stroke);
            _stroke.StrokeCap = SKStrokeCap.Butt;
        }

        private void DrawFrostParticle(SKCanvas c, in SkinParticle p, in SkinParticleState st, float x, float y, float s, float r, float a)
        {
            int save = c.Save();
            c.Translate(x, y);
            if (p.Kind == SkinParticleKind.Puff)
            {
                c.Scale(s, s);
                _fill.Shader = FrostPuff();
                _fill.Color = SKColors.White.WithAlpha(A(.6f * a));
                c.DrawCircle(0, 0, 1, _fill);
                _fill.Shader = null;
                c.RestoreToCount(save);
                return;
            }
            c.RotateRadians(st.Rot);
            _poly.Rewind();
            _stroke.StrokeCap = SKStrokeCap.Round;
            _stroke.Color = Tone(p.Argb, a);
            if (p.Kind == SkinParticleKind.Stick)
            {
                _poly.MoveTo(-s / 2, 0); _poly.LineTo(s / 2, 0);
                _stroke.StrokeWidth = Math.Max(1f, r * .034f);
            }
            else
            {
                // A small flake: three strokes crossing at sixty degrees.
                for (int i = 0; i < 3; i++)
                {
                    float q = i * MathF.PI / 3, dx = MathF.Cos(q) * s, dy = MathF.Sin(q) * s;
                    _poly.MoveTo(-dx, -dy); _poly.LineTo(dx, dy);
                }
                _stroke.StrokeWidth = Math.Max(.8f, s * .28f);
            }
            c.DrawPath(_poly, _stroke);
            _stroke.StrokeCap = SKStrokeCap.Butt;
            c.RestoreToCount(save);
        }
    }
}
