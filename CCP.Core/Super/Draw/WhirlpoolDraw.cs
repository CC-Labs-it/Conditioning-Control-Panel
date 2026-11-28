using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the <see cref="Whirlpool"/>: a soft glow and three spiral arms that thin and fade toward
/// the rim, so the pool has no hard edge. Skia only, no WPF and no App: the Avalonia head can take
/// this file as it is.
/// </summary>
public sealed class WhirlpoolDraw : IDisposable
{
    private const int Arms = 3;
    private readonly SKPaint _arm = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _path = new();
    private SKShader? _armShader;
    private readonly SKPaint _glow = new() { IsAntialias = false };
    private SKShader? _glowShader;
    private float _glowR; private int _glowKey;

    /// <summary><paramref name="strength"/> 0..1 is the base spiral's own opacity setting.</summary>
    /// <summary><paramref name="from"/> = the Loom spiral the pool wears (round, soft at the rim), melting
    /// into <paramref name="to"/> by <paramref name="mix"/>; with none, the three drawn arms stand in.
    /// <paramref name="phase"/> runs the spiral's pulse and wobble.</summary>
    /// <summary><paramref name="alpha"/> 0..1 fades the whole pool (a head's Gentle visuals fade in).</summary>
    public void Draw(SKCanvas canvas, Whirlpool p, double strength, LoomSpec? from = null, LoomSpec? to = null, double mix = 0, double phase = 0, double alpha = 1)
    {
        if (p.Size <= 0.004) return;
        double e = p.Eased;
        float r = (float)p.Radius, cx = (float)p.X, cy = (float)p.Y, dip = (float)p.Dip;
        // Pink when small, warming toward amber as it grows.
        float hue = (float)(330 + (384 - 330) * e) % 360;
        float master = (float)Math.Clamp(0.35 + 0.65 * strength, 0, 1) * (float)Math.Min(1, p.Size / 0.08) * (float)Math.Clamp(alpha, 0, 1);

        int key = (int)(hue * 4) ^ ((int)(e * 64) << 12);
        bool rebuilt = _glowShader == null || Math.Abs(_glowR - r) > 1.5f || key != _glowKey;
        if (rebuilt)
        {
            _glowShader?.Dispose();
            var core = SKColor.FromHsl(hue, 100, 80).WithAlpha((byte)(255 * (0.22 + 0.4 * e)));
            var mid = SKColor.FromHsl(hue, 90, 60).WithAlpha((byte)(255 * (0.08 + 0.14 * e)));
            _glowShader = SKShader.CreateRadialGradient(new SKPoint(0, 0), r,
                new[] { core, mid, SKColors.Transparent }, new[] { 0f, 0.28f, 1f }, SKShaderTileMode.Clamp);
            _glowR = r; _glowKey = key;
        }
        canvas.Save();
        canvas.Translate(cx, cy);
        _glow.Shader = _glowShader;
        _glow.Color = SKColors.White.WithAlpha((byte)(255 * master));
        canvas.DrawCircle(0, 0, r, _glow);

        if (from != null)
        {
            _loom ??= new LoomSpiral(LoomSize);
            _loomBmp ??= new SKBitmap(new SKImageInfo(LoomSize, LoomSize, SKColorType.Bgra8888, SKAlphaType.Premul));
            _loomPx ??= new byte[LoomSize * LoomSize * 4];
            double k = to == null ? 0 : Math.Clamp(mix, 0, 1);
            // The same picture is asked for once per monitor and again on every present another layer
            // causes: work it out only when what goes into it has moved.
            var made = (from, to, k, p.Angle, phase);
            if (!_loomMade.HasValue || !_loomMade.Value.Equals(made))
            {
                _loomMade = made;
                _loom.Clear();
                _loom.Add(from, p.Angle, phase, 1 - k);
                if (to != null) _loom.Add(to, p.Angle, phase, k);
                _loom.CopyTo(_loomPx);
                // The rim fade goes into the pixels, so the picture needs no offscreen layer to fade out.
                var rim = RimFade;
                for (int q = 0, c = 0; q < _loomPx.Length; q += 4, c++)
                {
                    int f = rim[c];
                    if (f == 255) continue;
                    _loomPx[q] = (byte)(_loomPx[q] * f / 255); _loomPx[q + 1] = (byte)(_loomPx[q + 1] * f / 255);
                    _loomPx[q + 2] = (byte)(_loomPx[q + 2] * f / 255); _loomPx[q + 3] = (byte)(_loomPx[q + 3] * f / 255);
                }
                // The compositor's fast stretch wants Bgra8888: swap red and blue.
                for (int q = 0; q < _loomPx.Length; q += 4) (_loomPx[q], _loomPx[q + 2]) = (_loomPx[q + 2], _loomPx[q]);
                System.Runtime.InteropServices.Marshal.Copy(_loomPx, 0, _loomBmp.GetPixels(), _loomPx.Length);
                _loomBmp.NotifyPixelsChanged();
            }
            DrawPicture(canvas, _loomBmp, r, master, (float)e);
            canvas.Restore();
            return;
        }

        // Each arm is ONE stroked path (no joints to show), and one radial shader fades all three
        // from full strength at 45% of the reach to nothing at the rim: a soft edge, no circle.
        double turns = p.Turns, pitch = r / (turns * 2 * Math.PI), end = turns * 2 * Math.PI;
        const double step = 0.05;
        _path.Rewind();
        for (int arm = 0; arm < Arms; arm++)
        {
            double off = arm * 2 * Math.PI / Arms - p.Angle;
            bool first = true;
            for (double th = 0.3; th < end; th += step)
            {
                // Not a ruler-drawn spiral: each arm strays a little and keeps morphing.
                double rr = pitch * th, a = th + off + Whirlpool.Wobble(rr / r, th + off, p.Angle);
                rr *= 1 + 0.07 * (rr / r) * Math.Sin(2 * a + p.Angle * 0.29);   // and breathes in and out a little
                float qx = (float)(Math.Cos(a) * rr), qy = (float)(Math.Sin(a) * rr);
                if (first) { _path.MoveTo(qx, qy); first = false; } else _path.LineTo(qx, qy);
            }
        }
        if (_armShader == null || rebuilt)
        {
            _armShader?.Dispose();
            var full = SKColor.FromHsl(hue, 92, 70).WithAlpha((byte)(255 * (0.4 + 0.5 * e)));
            _armShader = SKShader.CreateRadialGradient(new SKPoint(0, 0), r,
                new[] { full, full, full.WithAlpha((byte)(full.Alpha * 0.45f)), SKColors.Transparent },
                new[] { 0f, 0.45f, 0.72f, 1f }, SKShaderTileMode.Clamp);
        }
        _arm.Shader = _armShader;
        _arm.Color = SKColors.White.WithAlpha((byte)(255 * master));
        // A wide faint pass first, so the line has a soft shoulder instead of a hard edge.
        float width = (float)(5 + 13 * e) * dip;
        _arm.Color = SKColors.White.WithAlpha((byte)(255 * master * 0.28f));
        _arm.StrokeWidth = width * 2.6f;
        canvas.DrawPath(_path, _arm);
        _arm.Color = SKColors.White.WithAlpha((byte)(255 * master));
        _arm.StrokeWidth = width;
        canvas.DrawPath(_path, _arm);
        canvas.Restore();
    }

    // The Loom spiral is worked out small and stretched: it is all soft curves, and this keeps
    // a frame well under a millisecond or two on the CPU.
    private const int LoomSize = 320;
    private LoomSpiral? _loom;
    private SKBitmap? _loomBmp;
    private byte[]? _loomPx;
    private (LoomSpec From, LoomSpec? To, double Mix, double Angle, double Phase)? _loomMade;
    private readonly SKPaint _pic = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
    // The disc's fade over its outer part, per pixel of the small picture: full to 55% of the reach,
    // 120/255 at 80%, nothing at the rim.
    private static readonly byte[] RimFade = MakeRimFade();

    private static byte[] MakeRimFade()
    {
        var a = new byte[LoomSize * LoomSize];
        double half = LoomSize / 2.0;
        for (int y = 0, i = 0; y < LoomSize; y++)
            for (int x = 0; x < LoomSize; x++, i++)
            {
                double dx = (x + 0.5 - half) / half, dy = (y + 0.5 - half) / half, t = Math.Sqrt(dx * dx + dy * dy);
                double v = t <= 0.55 ? 1 : t <= 0.8 ? 1 + (120 / 255.0 - 1) * (t - 0.55) / 0.25 : t < 1 ? 120 / 255.0 * (1 - (t - 0.8) / 0.2) : 0;
                a[i] = (byte)Math.Round(v * 255);
            }
        return a;
    }

    /// <summary>The picture as a disc that fades out over its outer part (the fade is already in its
    /// pixels). The canvas is already centred on the pool.</summary>
    private void DrawPicture(SKCanvas canvas, SKBitmap picture, float r, float master, float e)
    {
        _pic.Color = SKColors.White.WithAlpha((byte)(255 * master * (0.55f + 0.45f * e)));
        canvas.DrawBitmap(picture, new SKRect(-r, -r, r, r), _pic);
    }

    public void Dispose()
    {
        _pic.Dispose();
        _loomBmp?.Dispose(); _loomBmp = null;
        _glowShader?.Dispose(); _glowShader = null;
        _armShader?.Dispose(); _armShader = null;
        _arm.Dispose(); _glow.Dispose(); _path.Dispose();
    }
}
