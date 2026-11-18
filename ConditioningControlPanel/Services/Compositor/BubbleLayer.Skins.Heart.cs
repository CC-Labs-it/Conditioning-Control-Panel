using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>The Heart skin's draw: a plump heart in place of the soap sprite (soft halo, red glass, a glow
/// and a small ripple heart per beat, a rim and a gloss), and the small hearts its pop throws.
/// Shapes and timing come from <see cref="BubbleSkins"/>; the path and shaders are built once, app-lifetime.</summary>
public sealed partial class BubbleLayer
{
    internal sealed partial class SkinPainter
    {
        private static SKPath? s_heartPath, s_heartArc;
        private static SKShader? s_heartFill, s_heartGlow;

        private static SKPath HeartPath()
        {
            if (s_heartPath != null) return s_heartPath;
            var pts = BubbleSkins.HeartOutline();
            var path = new SKPath();
            path.MoveTo(pts[0], pts[1]);
            for (int i = 2; i < pts.Length; i += 2) path.LineTo(pts[i], pts[i + 1]);
            path.Close();
            return s_heartPath = path;
        }

        private static SKShader HeartFill() => s_heartFill ??= SKShader.CreateTwoPointConicalGradient(
            new SKPoint(-.26f, -.32f), .05f, new SKPoint(0, 0), 1.02f,
            new[] { new SKColor(255, 176, 188, 128), new SKColor(240, 50, 88, 140), new SKColor(206, 18, 60, 168), new SKColor(255, 150, 172, 128) },
            new[] { 0f, .5f, .86f, 1f }, SKShaderTileMode.Clamp);

        private static SKShader HeartGlow() => s_heartGlow ??= SKShader.CreateRadialGradient(new SKPoint(0, 0), .8f,
            new[] { new SKColor(255, 130, 150, 255), new SKColor(255, 60, 90, 0) }, SKShaderTileMode.Clamp);

        /// <summary>The heart body at glass radius <paramref name="r"/>; <paramref name="squeeze"/> scales it
        /// (the pop), <paramref name="white"/> fills it white (the last moment of the pop, never under Gentle).</summary>
        private void DrawHeart(SKCanvas c, SkinPrim body, SkinPrim ripple, float cx, float cy, float r, float alpha,
                               float squeeze, float white)
        {
            float v = body.Alpha, s = r * body.R * squeeze;
            var path = HeartPath();
            int save = c.Save();
            c.Translate(cx, cy);
            c.Scale(s, s);
            _stroke.StrokeJoin = SKStrokeJoin.Round;
            // A soft halo instead of a hard edge.
            _stroke.Color = new SKColor(255, 90, 125, A((.12f + .1f * v) * alpha));
            _stroke.StrokeWidth = .2f;
            c.DrawPath(path, _stroke);
            _fill.Shader = HeartFill();
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawPath(path, _fill);

            int clip = c.Save();
            c.ClipPath(path, SKClipOperation.Intersect, true);
            _fill.Shader = HeartGlow();
            _fill.Color = SKColors.White.WithAlpha(A((.12f + .55f * v) * alpha));
            c.DrawRect(-1, -1, 2, 2, _fill);
            _fill.Shader = null;
            // Each beat sends a small heart from the middle to the wall.
            float es = Math.Max(.05f, ripple.R);
            c.Save();
            c.Scale(es, es);
            _stroke.Color = new SKColor(255, 210, 220, A(ripple.Alpha * alpha));
            _stroke.StrokeWidth = .03f / es;
            c.DrawPath(path, _stroke);
            c.Restore();
            _stroke.StrokeCap = SKStrokeCap.Round;
            _stroke.Color = SKColors.White.WithAlpha(A(.2f * alpha));
            _stroke.StrokeWidth = .06f;
            if (s_heartArc == null) { s_heartArc = new SKPath(); s_heartArc.AddArc(new SKRect(-.68f, -.73f, .68f, .63f), 36, 45); }
            c.DrawPath(s_heartArc, _stroke);
            _stroke.StrokeCap = SKStrokeCap.Butt;
            c.RestoreToCount(clip);

            // Rim and gloss.
            _stroke.Color = new SKColor(255, 200, 212, A(.4f * alpha));
            _stroke.StrokeWidth = .075f;
            c.DrawPath(path, _stroke);
            _stroke.Color = new SKColor(255, 236, 241, A(.55f * alpha));
            _stroke.StrokeWidth = .022f;
            c.DrawPath(path, _stroke);
            _stroke.StrokeJoin = SKStrokeJoin.Miter;
            Ellipse(c, -.4f, -.4f, .3f, .17f, -.62f, SKColors.White.WithAlpha(A(.2f * alpha)));
            Ellipse(c, -.43f, -.42f, .18f, .085f, -.62f, SKColors.White.WithAlpha(A(.66f * alpha)));
            _fill.Color = SKColors.White.WithAlpha(A(.45f * alpha));
            c.DrawCircle(-.63f, -.16f, .038f, _fill);
            if (white > .003f)
            {
                _fill.Color = SKColors.White.WithAlpha(A(Math.Min(1f, white) * alpha));
                c.DrawPath(path, _fill);
            }
            c.RestoreToCount(save);
        }

        private void Ellipse(SKCanvas c, float x, float y, float rx, float ry, float rot, SKColor col)
        {
            int save = c.Save();
            c.Translate(x, y);
            c.RotateRadians(rot);
            _fill.Color = col;
            c.DrawOval(0, 0, rx, ry, _fill);
            c.RestoreToCount(save);
        }

        private void DrawHeartParticle(SKCanvas c, in SkinParticle p, in SkinParticleState st, double age, float x, float y, float s, float a)
        {
            var path = HeartPath();
            int save = c.Save();
            c.Translate(x, y);
            if (p.Kind == SkinParticleKind.HeartWave)
            {
                float u = (float)(age / p.Life);
                c.Scale(s, s);
                // Thinner and softer than the lab's (.06): a field pops many at once.
                _stroke.Color = Tone(p.Argb, .8f * a);
                _stroke.StrokeWidth = Math.Max(.5f / s, .035f * (1 - u));
                c.DrawPath(path, _stroke);
            }
            else
            {
                c.RotateRadians(st.Rot);
                c.Scale(s, s);
                _fill.Color = Tone(p.Argb, a);
                c.DrawPath(path, _fill);
                if (s > 1.6f) Ellipse(c, -.38f, -.4f, .2f, .11f, -.6f, SKColors.White.WithAlpha(A(.55f * a)));
            }
            c.RestoreToCount(save);
        }
    }
}
