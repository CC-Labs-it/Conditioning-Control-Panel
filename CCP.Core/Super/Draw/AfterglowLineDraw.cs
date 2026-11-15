using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws an <see cref="AfterglowLine"/> on one monitor: each ghost rises big where its word
/// flashed, travels down to its place in a line near the bottom, and the full line glows and
/// sinks. Skia only, no WPF and no App: the Avalonia head can take this file as it is.
/// </summary>
public sealed class AfterglowLineDraw : IDisposable
{
    private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    private readonly SKPaint _glow = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    private readonly SKPaint _rule = new() { IsAntialias = false };
    private SKMaskFilter? _blur;
    private float _blurSigma = -1;
    private readonly float[] _widths = new float[AfterglowLine.MaxCapacity];

    /// <summary>
    /// <paramref name="bounds"/> is the monitor on the canvas. (<paramref name="fromX"/>,
    /// <paramref name="fromY"/>) is where a word flashes: the middle of the monitor, or the eye of
    /// the pool when it is up there.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect bounds, AfterglowLine line, SKTypeface? face, SKColor glowColour, float fromX, float fromY)
    {
        int n = line.Count;
        if (n == 0) return;
        float size = Math.Clamp(bounds.Height * 0.034f, 14f, 64f), big = size * 2.3f;
        _text.Typeface = face; _glow.Typeface = face;
        _text.TextSize = size;
        float gap = size * 0.6f, total = -gap;
        for (int i = 0; i < n; i++) { _widths[i] = _text.MeasureText(line.TextAt(i)); total += _widths[i] + gap; }
        float fit = total > bounds.Width * 0.84f ? bounds.Width * 0.84f / total : 1f;
        size *= fit; gap *= fit; total *= fit;
        float sink = (float)line.FinaleSinkOut, glowIn = (float)line.FinaleGlowIn;
        float baseY = bounds.Bottom - bounds.Height * 0.11f + sink * bounds.Height * 0.045f;
        float x = bounds.MidX - total / 2;

        if (!line.InFinale)
        {
            _rule.Color = SKColors.White.WithAlpha(40);
            canvas.DrawRect(x - size * 0.4f, baseY + size * 0.45f, total + size * 0.8f, Math.Max(1f, size * 0.05f), _rule);
        }
        if (line.InFinale)
        {
            float sigma = size * 0.35f;
            if (_blur == null || Math.Abs(sigma - _blurSigma) > 0.5f)
            {
                _blur?.Dispose();
                _blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
                _blurSigma = sigma;
            }
            _glow.MaskFilter = _blur;
        }

        for (int i = 0; i < n; i++)
        {
            float w = _widths[i] * fit, slotX = x + w / 2;
            x += w + gap;
            float t = (float)line.Travel(i), a = (float)Math.Clamp(line.Alpha(i), 0, 1);
            if (a <= 0.004f) continue;
            float px = fromX + (slotX - fromX) * t, py = fromY + (baseY - fromY) * t;
            float s = (big * fit + (size - big * fit) * t) * (1 + 0.1f * glowIn * (1 - sink));
            string word = line.TextAt(i);
            if (line.InFinale)
            {
                _glow.TextSize = s;
                _glow.Color = glowColour.WithAlpha((byte)(210 * glowIn * (1 - sink)));
                canvas.DrawText(word, px, py, _glow);
            }
            _text.TextSize = s;
            _text.Color = SKColors.White.WithAlpha((byte)(255 * a));
            canvas.DrawText(word, px, py, _text);
        }
    }

    public void Dispose()
    {
        _blur?.Dispose(); _blur = null;
        _text.Dispose(); _glow.Dispose(); _rule.Dispose();
    }
}
