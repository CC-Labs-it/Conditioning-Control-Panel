using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>The Marble skin's draw: a solid glass body in place of the soap sprite with the winding vanes inside,
/// a rim, a gloss and a caustic; the cracks and the flash of its pre-burst; its shards and glints.
/// Shapes and colours come from <see cref="BubbleSkins"/>; shaders and the clip are built once, app-lifetime.</summary>
public sealed partial class BubbleLayer
{
    internal sealed partial class SkinPainter
    {
        private static SKShader? s_marbleBody, s_marbleCaustic;
        private static SKPath? s_marbleClip;

        private readonly SkinQuad[] _quads = new SkinQuad[BubbleSkins.MaxMarbleQuads];
        private readonly SKPath _poly = new();
        private readonly SKPaint _quadPaint = new() { IsAntialias = true, Style = SKPaintStyle.StrokeAndFill, StrokeJoin = SKStrokeJoin.Round };
        private readonly float[] _cracks = new float[BubbleSkins.MarbleCrackCount * BubbleSkins.MarbleCrackStops * 2];
        private readonly float[] _shard = new float[8];
        private int _crackSeed = int.MinValue;

        partial void DisposeParts()
        {
            _poly.Dispose();
            _quadPaint.Dispose();
        }

        private static SKShader MarbleBody() => s_marbleBody ??= SKShader.CreateTwoPointConicalGradient(
            new SKPoint(-.32f, -.38f), .05f, new SKPoint(0, 0), 1f,
            new[] { new SKColor(255, 255, 255, 153), new SKColor(196, 220, 240, 102), new SKColor(104, 140, 186, 158), new SKColor(232, 242, 255, 217) },
            new[] { 0f, .45f, .9f, 1f }, SKShaderTileMode.Clamp);

        private static SKShader MarbleCaustic() => s_marbleCaustic ??= SKShader.CreateRadialGradient(new SKPoint(.42f, .46f), .42f,
            new[] { new SKColor(255, 255, 255, 107), new SKColor(255, 255, 255, 0) }, SKShaderTileMode.Clamp);

        private void Cracks(int seed)
        {
            if (seed == _crackSeed) return;
            BubbleSkins.MarbleCracks(seed, _cracks);
            _crackSeed = seed;
        }

        /// <summary>The marble at glass radius <paramref name="r"/>. <paramref name="k"/> &gt; 0 is the pre-burst: it
        /// shakes, cracks from the middle and flashes white at the end (a slow soft glow and no shake under Gentle).</summary>
        private void DrawMarble(SKCanvas c, int seed, double seconds, double motion, float cx, float cy, float r, float alpha, float k, bool gentle)
        {
            int save = c.Save();
            c.Translate(cx, cy);
            if (k > 0 && !gentle)
            {
                var (sx, sy) = BubbleSkins.MarbleShake(seed, seconds);
                c.Translate((float)sx * r, (float)sy * r);
            }
            c.Scale(r, r);
            // The glass, with a breath of the colour inside.
            _fill.Shader = MarbleBody();
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawCircle(0, 0, 1, _fill);
            _fill.Shader = null;
            var (tr, tg, tb) = BubbleSkins.MarbleTint(seed, seconds, motion);
            _fill.Color = new SKColor((byte)tr, (byte)tg, (byte)tb, A(.14f * alpha));
            c.DrawCircle(0, 0, .8f, _fill);

            int clip = c.Save();
            c.ClipPath(s_marbleClip ??= Circle(.9f), SKClipOperation.Intersect, true);
            c.RotateRadians((float)BubbleSkins.MarbleTumble(seed, seconds, motion));
            int n = BubbleSkins.MarbleVanes(seed, seconds, motion, r, _quads);
            _quadPaint.StrokeWidth = .7f / r;   // closes the seams between patches
            for (int i = 0; i < n; i++)
            {
                var q = _quads[i];
                _poly.Rewind();
                _poly.MoveTo(q.X0, q.Y0); _poly.LineTo(q.X1, q.Y1); _poly.LineTo(q.X2, q.Y2); _poly.LineTo(q.X3, q.Y3); _poly.Close();
                _quadPaint.Color = Tone(q.Argb, alpha);
                c.DrawPath(_poly, _quadPaint);
            }
            c.RotateRadians(-(float)BubbleSkins.MarbleTumble(seed, seconds, motion));
            _stroke.Color = new SKColor(30, 58, 100, A(.34f * alpha));
            _stroke.StrokeWidth = .12f;
            c.DrawArc(new SKRect(-.84f, -.84f, .84f, .84f), 90, 108, false, _stroke);
            _fill.Shader = MarbleCaustic();
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawCircle(.42f, .46f, .42f, _fill);
            _fill.Shader = null;
            c.RestoreToCount(clip);

            // Rim, outline, gloss, a highlight arc and a spark.
            _stroke.Color = SKColors.White.WithAlpha(A(.8f * alpha));
            _stroke.StrokeWidth = .05f;
            c.DrawCircle(0, 0, .975f, _stroke);
            _stroke.Color = new SKColor(30, 50, 80, A(.35f * alpha));
            _stroke.StrokeWidth = 1f / r;
            c.DrawCircle(0, 0, 1, _stroke);
            Ellipse(c, (float)(Math.Sin(.62) * -.6), (float)(-Math.Cos(.62) * .6), .26f, .11f, -.62f, SKColors.White.WithAlpha(A(.95f * alpha)));
            _stroke.StrokeCap = SKStrokeCap.Round;
            _stroke.Color = SKColors.White.WithAlpha(A(.35f * alpha));
            _stroke.StrokeWidth = .03f;
            c.DrawArc(new SKRect(-.8f, -.8f, .8f, .8f), -165.6f, 43.2f, false, _stroke);
            _fill.Color = SKColors.White.WithAlpha(A(.7f * alpha));
            c.DrawCircle(-.2f, -.72f, .035f, _fill);

            if (k > 0)
            {
                Cracks(seed);
                _stroke.StrokeJoin = SKStrokeJoin.Round;
                _stroke.Color = SKColors.White.WithAlpha(A(.95f * alpha));
                _stroke.StrokeWidth = Math.Max(.8f / r, .022f);
                float reach = Math.Min(1f, k * 1.5f) * 4;
                for (int line = 0; line < BubbleSkins.MarbleCrackCount; line++)
                {
                    int o = line * BubbleSkins.MarbleCrackStops * 2;
                    _poly.Rewind();
                    _poly.MoveTo(0, 0);
                    for (int s = 1; s <= 4; s++)
                    {
                        float f = Math.Clamp(reach - (s - 1), 0, 1);
                        if (f <= 0) break;
                        float x0 = _cracks[o + (s - 1) * 2], y0 = _cracks[o + (s - 1) * 2 + 1], x1 = _cracks[o + s * 2], y1 = _cracks[o + s * 2 + 1];
                        _poly.LineTo(x0 + (x1 - x0) * f, y0 + (y1 - y0) * f);
                    }
                    c.DrawPath(_poly, _stroke);
                }
                _stroke.StrokeJoin = SKStrokeJoin.Miter;
                float flash = gentle ? .3f * k * k : k > .75f ? (k - .75f) * 2.4f : 0;
                if (flash > .003f)
                {
                    _fill.Color = SKColors.White.WithAlpha(A(flash * alpha));
                    c.DrawCircle(0, 0, 1, _fill);
                }
            }
            _stroke.StrokeCap = SKStrokeCap.Butt;
            c.RestoreToCount(save);
        }

        private static SKPath Circle(float radius)
        {
            var p = new SKPath();
            p.AddCircle(0, 0, radius);
            return p;
        }

        private void DrawMarbleParticle(SKCanvas c, in SkinParticle p, in SkinParticleState st, int seed, float x, float y, float s, float r, float a)
        {
            int save = c.Save();
            c.Translate(x, y);
            if (p.Kind == SkinParticleKind.Shard)
            {
                Cracks(seed);
                int k = BubbleSkins.MarbleShard(_cracks, p.Index, _shard);
                float mx = 0, my = 0;
                for (int i = 0; i < k; i++) { mx += _shard[i * 2]; my += _shard[i * 2 + 1]; }
                mx /= k; my /= k;
                c.RotateRadians(st.Rot);
                _poly.Rewind();
                for (int i = 0; i < k; i++)
                {
                    float px = (_shard[i * 2] - mx) * r, py = (_shard[i * 2 + 1] - my) * r;
                    if (i == 0) _poly.MoveTo(px, py); else _poly.LineTo(px, py);
                }
                _poly.Close();
                _fill.Color = Tone(p.Argb, a);
                c.DrawPath(_poly, _fill);
                _stroke.Color = SKColors.White.WithAlpha(A(.85f * a));
                _stroke.StrokeWidth = 1f;
                c.DrawPath(_poly, _stroke);
            }
            else
            {
                // A four-point glint (the lab's star4).
                _poly.Rewind();
                _poly.MoveTo(0, -s);
                _poly.QuadTo(0, 0, s, 0); _poly.QuadTo(0, 0, 0, s); _poly.QuadTo(0, 0, -s, 0); _poly.QuadTo(0, 0, 0, -s);
                _poly.Close();
                _fill.Color = Tone(p.Argb, a);
                c.DrawPath(_poly, _fill);
            }
            c.RestoreToCount(save);
        }
    }
}
