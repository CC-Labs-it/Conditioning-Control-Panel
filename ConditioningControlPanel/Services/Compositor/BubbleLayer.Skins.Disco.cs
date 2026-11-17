using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>The Disco skin's draw: a mirror ball in place of the soap sprite (dark body, palette tiles lit by two
/// lights and their own shimmer, a limb shade, a faint rim, star glints riding the tiles), the rays and light spots
/// it throws, the colour flare of its pre-burst (never white, none under Gentle), and its beams and mirror shards.
/// Shapes and colours come from <see cref="BubbleSkins"/> and the bubble's <see cref="DiscoPalette"/>; shaders are
/// built once per colour and kept (a few dozen at most), never per frame.</summary>
public sealed partial class BubbleLayer
{
    internal sealed partial class SkinPainter
    {
        private const int MaxFadeShaders = 48;
        private static SKShader? s_discoBody, s_discoShade;
        /// <summary>Colour to transparent along x 0..1, one per colour a ray or a beam wears. UI thread only.</summary>
        private static readonly Dictionary<uint, SKShader> s_fade = new();

        private readonly SkinQuad[] _discoQuads = new SkinQuad[BubbleSkins.MaxDiscoFacets];

        private static SKShader DiscoBody() => s_discoBody ??= SKShader.CreateTwoPointConicalGradient(
            new SKPoint(-.3f, -.35f), .05f, new SKPoint(0, 0), 1f,
            new[] { new SKColor(0x3a, 0x36, 0x44), new SKColor(0x09, 0x08, 0x0d) }, null, SKShaderTileMode.Clamp);

        private static SKShader DiscoShade() => s_discoShade ??= SKShader.CreateRadialGradient(new SKPoint(0, 0), (float)BubbleSkins.DiscoBall,
            new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 120) }, new[] { .55f, 1f }, SKShaderTileMode.Clamp);

        private static SKShader Fade(uint argb)
        {
            uint rgb = argb & 0xFFFFFF;
            if (s_fade.TryGetValue(rgb, out var sh)) return sh;
            if (s_fade.Count >= MaxFadeShaders)
            {
                foreach (var old in s_fade.Values) old.Dispose();
                s_fade.Clear();
            }
            var c = new SKColor(rgb | 0xFF000000);
            sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), new[] { c, c.WithAlpha(0) }, SKShaderTileMode.Clamp);
            s_fade[rgb] = sh;
            return sh;
        }

        /// <summary>The ball at glass radius <paramref name="r"/>. <paramref name="k"/> &gt; 0 is the pre-burst: every
        /// tile flares into its own colour (under Gentle the ball only holds).</summary>
        private void DrawDisco(SKCanvas c, int seed, double seconds, double motion, float cx, float cy, float r, float alpha, float k, bool gentle,
                               DiscoPalette? palette)
        {
            int np = BubbleSkins.Interior(BubbleSkinIds.Disco, seed, seconds, motion, r, _prims, palette);
            int save = c.Save();
            c.Translate(cx, cy);
            c.Scale(r, r);

            // Thrown light first: the rays leave from behind the limb, the spots circle outside.
            for (int i = 0; i < np; i++)
            {
                var p = _prims[i];
                float a = p.Alpha * alpha;
                if (a <= .004f) continue;
                if (p.Kind == SkinPrimKind.Ray)
                {
                    int s2 = c.Save();
                    c.Translate(p.X, p.Y);
                    c.RotateRadians(MathF.Atan2(p.Y, p.X));
                    c.Scale(p.R, p.R);
                    _poly.Rewind();
                    _poly.MoveTo(0, -.09f); _poly.LineTo(1, -.02f); _poly.LineTo(1, .02f); _poly.LineTo(0, .09f); _poly.Close();
                    _fill.Shader = Fade(p.Argb);
                    _fill.Color = SKColors.White.WithAlpha(A(a));
                    c.DrawPath(_poly, _fill);
                    _fill.Shader = null;
                    c.RestoreToCount(s2);
                }
                else if (p.Kind == SkinPrimKind.Fleck)
                {
                    _fill.Color = Tone(p.Argb, a * .3f);
                    c.DrawCircle(p.X, p.Y, p.R * 2.2f, _fill);
                    _fill.Color = Tone(p.Argb, a);
                    c.DrawCircle(p.X, p.Y, p.R, _fill);
                }
            }

            float ball = (float)BubbleSkins.DiscoBall;
            _fill.Shader = DiscoBody();
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawCircle(0, 0, 1, _fill);
            _fill.Shader = null;
            int n = BubbleSkins.DiscoFacets(seed, seconds, motion, r, _discoQuads, palette, gentle ? 0 : k);
            for (int i = 0; i < n; i++)
            {
                var q = _discoQuads[i];
                _poly.Rewind();
                _poly.MoveTo(q.X0, q.Y0); _poly.LineTo(q.X1, q.Y1); _poly.LineTo(q.X2, q.Y2); _poly.LineTo(q.X3, q.Y3); _poly.Close();
                _fill.Color = Tone(q.Argb, alpha);
                c.DrawPath(_poly, _fill);
            }
            _fill.Shader = DiscoShade();
            _fill.Color = SKColors.White.WithAlpha(A(alpha * (1 - .3f * (gentle ? 0 : k))));
            c.DrawCircle(0, 0, ball, _fill);
            _fill.Shader = null;
            _stroke.Color = SKColors.White.WithAlpha(A(.22f * alpha));
            _stroke.StrokeWidth = 1f / r;
            c.DrawCircle(0, 0, ball, _stroke);

            for (int i = 0; i < np; i++)
            {
                var p = _prims[i];
                if (p.Kind != SkinPrimKind.Glint || p.Alpha <= .004f) continue;
                Star4(c, p.X, p.Y, p.R, Tone(p.Argb, .95f * p.Alpha * alpha));
                _fill.Color = SKColors.White.WithAlpha(A(.9f * p.Alpha * alpha));
                c.DrawCircle(p.X, p.Y, p.R * .16f, _fill);
            }
            c.RestoreToCount(save);
        }

        /// <summary>The lab's star4: four points pulled in by quadratic curves.</summary>
        private void Star4(SKCanvas c, float x, float y, float s, SKColor col)
        {
            _poly.Rewind();
            _poly.MoveTo(x, y - s);
            _poly.QuadTo(x, y, x + s, y); _poly.QuadTo(x, y, x, y + s); _poly.QuadTo(x, y, x - s, y); _poly.QuadTo(x, y, x, y - s);
            _poly.Close();
            _fill.Color = col;
            c.DrawPath(_poly, _fill);
        }

        private void DrawDiscoParticle(SKCanvas c, in SkinParticle p, in SkinParticleState st, double age, float x, float y, float s, float a)
        {
            int save = c.Save();
            c.Translate(x, y);
            c.RotateRadians(st.Rot);
            if (p.Kind == SkinParticleKind.Square)
            {
                // A tumbling mirror shard in its colour: it narrows as it turns and, facing us, its colour warms toward
                // white and back, never switched (steady under Gentle: Grow 0).
                float f = p.Grow > 0 ? Math.Abs(MathF.Sin(st.Rot * p.Grow)) : .6f, h = .3f + .7f * f;
                float catchLight = p.Grow > 0 ? MathF.Pow(f, 6) * .6f : 0;
                var col = new SKColor(p.Argb);
                col = new SKColor((byte)(col.Red + (255 - col.Red) * catchLight), (byte)(col.Green + (255 - col.Green) * catchLight),
                                  (byte)(col.Blue + (255 - col.Blue) * catchLight));
                _fill.Color = col.WithAlpha(A(a * (.45f + .55f * f)));
                c.DrawRect(-s / 2, -s / 2 * h, s, s * h, _fill);
            }
            else
            {
                // A thin wedge that fades out to its tip (the gradient runs the beam's own length, so no blunt end).
                float u = (float)(age / p.Life), len = .4f + u;
                c.Scale(s * len, s);
                _poly.Rewind();
                _poly.MoveTo(0, 0); _poly.LineTo(1, -.018f); _poly.LineTo(1, .018f); _poly.Close();
                _fill.Shader = Fade(p.Argb);
                _fill.Color = SKColors.White.WithAlpha(A(.7f * a * (p.Argb >> 24) / 255f));
                c.DrawPath(_poly, _fill);
                _fill.Shader = null;
            }
            c.RestoreToCount(save);
        }
    }
}
