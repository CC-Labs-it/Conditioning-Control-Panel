using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>The Disco skin's draw: a mirror ball in place of the soap sprite (dark body, facets lit by two lights,
/// a limb shade, a faint rim, at most two glints), the flecks orbiting it, the white of its pre-burst (never under
/// Gentle), and its beams and mirror squares. Shapes and colours come from <see cref="BubbleSkins"/>; shaders are
/// built once, app-lifetime.</summary>
public sealed partial class BubbleLayer
{
    internal sealed partial class SkinPainter
    {
        private static SKShader? s_discoBody, s_discoShade, s_beamWhite, s_beamPink;

        private readonly SkinQuad[] _discoQuads = new SkinQuad[BubbleSkins.MaxDiscoFacets];

        private static SKShader DiscoBody() => s_discoBody ??= SKShader.CreateTwoPointConicalGradient(
            new SKPoint(-.3f, -.35f), .05f, new SKPoint(0, 0), 1f,
            new[] { new SKColor(0x3a, 0x36, 0x44), new SKColor(0x09, 0x08, 0x0d) }, null, SKShaderTileMode.Clamp);

        private static SKShader DiscoShade() => s_discoShade ??= SKShader.CreateRadialGradient(new SKPoint(0, 0), (float)BubbleSkins.DiscoBall,
            new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 140) }, new[] { .5f, 1f }, SKShaderTileMode.Clamp);

        private static SKShader Beam(bool pink) => pink
            ? s_beamPink ??= SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0),
                new[] { new SKColor(255, 90, 170, 230), new SKColor(255, 255, 255, 0) }, SKShaderTileMode.Clamp)
            : s_beamWhite ??= SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0),
                new[] { new SKColor(255, 255, 255, 230), new SKColor(255, 255, 255, 0) }, SKShaderTileMode.Clamp);

        /// <summary>The ball at glass radius <paramref name="r"/>. <paramref name="k"/> &gt; 0 is the pre-burst: the ball
        /// whitens (it only holds under Gentle).</summary>
        private void DrawDisco(SKCanvas c, int seed, double seconds, double motion, float cx, float cy, float r, float alpha, float k, bool gentle)
        {
            int np = BubbleSkins.Interior(BubbleSkinIds.Disco, seed, seconds, motion, r, _prims);
            int save = c.Save();
            c.Translate(cx, cy);
            c.Scale(r, r);
            for (int i = 0; i < np; i++)
            {
                var p = _prims[i];
                if (p.Kind is not (SkinPrimKind.Fleck or SkinPrimKind.FleckPink)) continue;
                _fill.Color = p.Kind == SkinPrimKind.FleckPink ? new SKColor(255, 90, 170, A(p.Alpha * alpha)) : SKColors.White.WithAlpha(A(p.Alpha * alpha));
                c.DrawCircle(p.X, p.Y, p.R, _fill);
            }

            float ball = (float)BubbleSkins.DiscoBall;
            _fill.Shader = DiscoBody();
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawCircle(0, 0, 1, _fill);
            _fill.Shader = null;
            int n = BubbleSkins.DiscoFacets(seed, seconds, motion, r, _discoQuads);
            for (int i = 0; i < n; i++)
            {
                var q = _discoQuads[i];
                _poly.Rewind();
                _poly.MoveTo(q.X0, q.Y0); _poly.LineTo(q.X1, q.Y1); _poly.LineTo(q.X2, q.Y2); _poly.LineTo(q.X3, q.Y3); _poly.Close();
                _fill.Color = Tone(q.Argb, alpha);
                c.DrawPath(_poly, _fill);
            }
            _fill.Shader = DiscoShade();
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawCircle(0, 0, ball, _fill);
            _fill.Shader = null;
            _stroke.Color = SKColors.White.WithAlpha(A(.22f * alpha));
            _stroke.StrokeWidth = 1f / r;
            c.DrawCircle(0, 0, ball, _stroke);

            for (int i = 0; i < np; i++)
            {
                var p = _prims[i];
                if (p.Kind is not (SkinPrimKind.Glint or SkinPrimKind.GlintPink) || p.Alpha <= .004f) continue;
                Star4(c, p.X, p.Y, p.R, p.Kind == SkinPrimKind.Glint
                    ? SKColors.White.WithAlpha(A(.95f * p.Alpha * alpha)) : new SKColor(255, 150, 205, A(.95f * p.Alpha * alpha)));
            }
            if (k > 0 && !gentle)
            {
                _fill.Color = SKColors.White.WithAlpha(A(k * .9f * alpha));
                c.DrawCircle(0, 0, 1, _fill);
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
                // A tumbling mirror: it narrows and catches the light as it turns (steady under Gentle: Grow 0).
                float f = p.Grow > 0 ? Math.Abs(MathF.Sin(st.Rot * p.Grow)) : .6f, h = .3f + .7f * f;
                _fill.Color = p.Grow > 0 && f > .8f ? SKColors.White.WithAlpha(A(a * (.35f + .65f * f))) : Tone(p.Argb, a * (.35f + .65f * f));
                c.DrawRect(-s / 2, -s / 2 * h, s, s * h, _fill);
            }
            else
            {
                // A thin wedge that fades out to its tip (the gradient runs the beam's own length, so no blunt end).
                float u = (float)(age / p.Life), len = .4f + u;
                c.Scale(s * len, s);
                _poly.Rewind();
                _poly.MoveTo(0, 0); _poly.LineTo(1, -.018f); _poly.LineTo(1, .018f); _poly.Close();
                _fill.Shader = Beam(((p.Argb >> 8) & 255) < 200);
                _fill.Color = SKColors.White.WithAlpha(A(.7f * a));
                c.DrawPath(_poly, _fill);
                _fill.Shader = null;
            }
            c.RestoreToCount(save);
        }
    }
}
