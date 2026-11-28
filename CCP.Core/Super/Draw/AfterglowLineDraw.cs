using System;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Looks;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws an <see cref="AfterglowLine"/> on one monitor: each ghost rises big where its word
/// flashed, travels down to its place in a line near the bottom, and the full line glows and
/// sinks. Skia only, no WPF and no App: the Avalonia head can take this file as it is (with
/// Services/Looks/WordStyleSkia.cs for a worn word style).
/// A worn word style (shop slot <c>word</c>) draws each ghost in the style's face and colour, and
/// plays the style's way in (typed, chalked, drifted, echoed, lit) as the ghost rises and travels;
/// it is settled once the ghost lands (<see cref="AfterglowLine.StyleClock"/>). Classic draws
/// exactly the plain white words.
/// </summary>
public sealed class AfterglowLineDraw : IDisposable
{
    private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    private readonly SKPaint _glow = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    private readonly SKPaint _rule = new() { IsAntialias = false };
    private SKMaskFilter? _blur;
    private float _blurSigma = -1;
    private readonly float[] _widths = new float[AfterglowLine.MaxCapacity];

    // The styled path: each ghost's laid-out letters, built when its text, style, face or size
    // changes (never per frame), one reused frame and the small per-letter fade layer paint.
    private readonly (StyledLine? Line, string? Text, WordStyle Style, SKTypeface? Face, float Size)[] _cache =
        new (StyledLine?, string?, WordStyle, SKTypeface?, float)[AfterglowLine.MaxCapacity];
    private readonly WordFrame _frame = new();
    private readonly SKPaint _layer = new();
    private SKMaskFilter? _styledBlur;
    private float _styledSigma = -1;

    /// <summary>The style's outline (chalk's board edge, the sign's frame), in the card's DIP offsets.</summary>
    private static readonly (float X, float Y)[] Outline =
    {
        (-3, -3), (3, -3), (-3, 3), (3, 3), (0, -4), (0, 4), (-4, 0), (4, 0)
    };

    /// <summary>Space between two words, in text heights (was 0.6: neighbours touched once the finale glow grew them, ccp-bugs #1684).</summary>
    internal const float WordGap = 0.9f;

    /// <summary>How much the finale grows each word: up to 10 % at the glow, back to 1 as the line sinks.</summary>
    internal static float FinaleGrow(float glowIn, float sink) => 1 + 0.1f * glowIn * (1 - sink);

    /// <summary>
    /// A word's slot spread away from the line's middle by the same factor its word grows, so the
    /// gaps grow with the words instead of each word swelling into its neighbour.
    /// </summary>
    internal static float SpreadSlot(float slotX, float midX, float grow) => midX + (slotX - midX) * grow;

    private static readonly SKTypeface StockFace = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>The face the line's words are drawn in: the player's Subliminal Font once they picked one
    /// (tier-2 report, 7.3.2: the setting did not reach V2), the stock Segoe UI Bold while it is on its default.</summary>
    public static SKTypeface FaceFor(string? subliminalFont)
    {
        var name = subliminalFont?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, "Arial", StringComparison.OrdinalIgnoreCase)) return StockFace;
        return ConditioningControlPanel.Services.Compositor.SubliminalLayer.ResolveFace(name);
    }

    /// <summary>
    /// <paramref name="bounds"/> is the monitor on the canvas. (<paramref name="fromX"/>,
    /// <paramref name="fromY"/>) is where a word flashes: the middle of the monitor, or the eye of
    /// the pool when it is up there. <paramref name="style"/> is the worn word style and
    /// <paramref name="motion"/> how much it may move (Still under Gentle visuals or motion Off).
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect bounds, AfterglowLine line, SKTypeface? face, SKColor glowColour, float fromX, float fromY,
        WordStyle style = WordStyle.Classic, WordMotion motion = WordMotion.Full)
    {
        int n = line.Count;
        if (n == 0) return;
        if (style != WordStyle.Classic) { DrawStyled(canvas, bounds, line, face, glowColour, fromX, fromY, style, motion); return; }
        float size = Math.Clamp(bounds.Height * 0.034f, 14f, 64f), big = size * 2.3f;
        _text.Typeface = face; _glow.Typeface = face;
        _text.TextSize = size;
        float gap = size * WordGap, total = -gap;
        for (int i = 0; i < n; i++) { _widths[i] = _text.MeasureText(line.TextAt(i)); total += _widths[i] + gap; }
        float fit = total > bounds.Width * 0.84f ? bounds.Width * 0.84f / total : 1f;
        size *= fit; gap *= fit; total *= fit;
        float sink = (float)line.FinaleSinkOut, glowIn = (float)line.FinaleGlowIn;
        float baseY = bounds.Bottom - bounds.Height * 0.11f + sink * bounds.Height * 0.045f;
        float x = bounds.MidX - total / 2;
        float grow = FinaleGrow(glowIn, sink);

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
            float w = _widths[i] * fit, slotX = SpreadSlot(x + w / 2, bounds.MidX, grow);
            x += w + gap;
            float t = (float)line.Travel(i), a = (float)Math.Clamp(line.Alpha(i), 0, 1);
            if (a <= 0.004f) continue;
            float px = fromX + (slotX - fromX) * t, py = fromY + (baseY - fromY) * t;
            float s = (big * fit + (size - big * fit) * t) * grow;
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

    /// <summary>
    /// The same line, worn: each ghost is a <see cref="StyledLine"/> laid out once at the line's
    /// own size and scaled as it travels, so its glyphs are measured once, not per frame.
    /// </summary>
    private void DrawStyled(SKCanvas canvas, SKRect bounds, AfterglowLine line, SKTypeface? face, SKColor glowColour,
        float fromX, float fromY, WordStyle style, WordMotion motion)
    {
        int n = line.Count;
        var look = WordStyles.FaceOf(style);
        var own = face ?? SKTypeface.Default;
        var drawFace = WordStyleSkia.FaceFor(look, own);
        float size0 = Math.Clamp(bounds.Height * 0.034f, 14f, 64f);
        float laid = (float)(size0 * look.Size);
        float gap = size0 * WordGap, total = -gap;
        for (int i = 0; i < n; i++)
        {
            var sl = LineFor(i, line.TextAt(i), style, drawFace, look, laid);
            _widths[i] = sl.Width;
            total += sl.Width + gap;
        }
        float fit = total > bounds.Width * 0.84f ? bounds.Width * 0.84f / total : 1f;
        float size = size0 * fit, big = size0 * 2.3f;
        gap *= fit; total *= fit;
        float sink = (float)line.FinaleSinkOut, glowIn = (float)line.FinaleGlowIn;
        float baseY = bounds.Bottom - bounds.Height * 0.11f + sink * bounds.Height * 0.045f;
        float x = bounds.MidX - total / 2;
        float grow = FinaleGrow(glowIn, sink);

        if (!line.InFinale)
        {
            _rule.Color = SKColors.White.WithAlpha(40);
            canvas.DrawRect(x - size * 0.4f, baseY + size * 0.45f, total + size * 0.8f, Math.Max(1f, size * 0.05f), _rule);
        }
        if (line.InFinale)
        {
            // In the line's own units (the canvas scale carries it to the screen).
            float sigma = laid * 0.35f;
            if (_styledBlur == null || Math.Abs(sigma - _styledSigma) > 0.5f)
            {
                _styledBlur?.Dispose();
                _styledBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
                _styledSigma = sigma;
            }
        }

        var fill = look.Colour is uint c ? WordStyleSkia.Colour(c) : SKColors.White;
        var border = look.Border is uint b ? WordStyleSkia.Colour(b) : SKColors.Transparent;
        var offsets = look.Border != null ? Outline : Array.Empty<(float X, float Y)>();
        for (int i = 0; i < n; i++)
        {
            float w = _widths[i] * fit, slotX = SpreadSlot(x + w / 2, bounds.MidX, grow);
            x += w + gap;
            float t = (float)line.Travel(i), a = (float)Math.Clamp(line.Alpha(i), 0, 1);
            if (a <= 0.004f) continue;
            var sl = _cache[i].Line!;
            float px = fromX + (slotX - fromX) * t, py = fromY + (baseY - fromY) * t;
            float s = (big * fit + (size - big * fit) * t) * grow;
            float k = s / size0;
            WordStyles.Fill(style, sl.Count, line.StyleClock(i), AfterglowLine.StyleLife, motion, _frame);

            float left = -sl.Width / 2f, mid = (sl.Ascent + sl.Descent) / 2f;
            canvas.Save();
            canvas.Translate(px, py);
            canvas.Scale(k);
            if (look.Tilt != 0) canvas.RotateDegrees((float)look.Tilt, 0, mid);
            if (line.InFinale && _styledBlur != null)
            {
                // The finale's glow: the word again, blurred, in the filter's colour, under it.
                _glow.MaskFilter = _styledBlur;
                _glow.TextAlign = SKTextAlign.Left;
                _glow.TextSize = sl.Size;
                _glow.Color = glowColour.WithAlpha((byte)(210 * glowIn * (1 - sink)));
                for (int j = 0; j < sl.Count; j++)
                {
                    _glow.Typeface = sl.Faces[j];
                    canvas.DrawText(sl.Parts[j], left + sl.At[j], 0, _glow);
                }
                _glow.TextAlign = SKTextAlign.Center;
                _glow.MaskFilter = null;
            }
            WordStyleSkia.DrawGhosts(canvas, sl, left, 0, 0, mid, _frame, fill, _text, a);
            WordStyleSkia.DrawLetters(canvas, sl, left, 0, _frame, look, fill, border, offsets, sl.Size / 90f, a, _text, _layer);
            WordStyleSkia.DrawCursor(canvas, sl, left, 0, _frame, fill, _text, a);
            canvas.Restore();
        }
    }

    private StyledLine LineFor(int i, string text, WordStyle style, SKTypeface face, WordFace look, float size)
    {
        var c = _cache[i];
        if (c.Line == null || !ReferenceEquals(c.Text, text) || c.Style != style || !ReferenceEquals(c.Face, face) || c.Size != size)
            _cache[i] = c = (StyledLine.Build(WordStyles.TextFor(style, text), face, look.Bold ? SKFontStyle.Bold : SKFontStyle.Normal, size),
                text, style, face, size);
        return c.Line!;
    }

    public void Dispose()
    {
        _blur?.Dispose(); _blur = null;
        _styledBlur?.Dispose(); _styledBlur = null;
        _text.Dispose(); _glow.Dispose(); _rule.Dispose(); _layer.Dispose();
    }
}
