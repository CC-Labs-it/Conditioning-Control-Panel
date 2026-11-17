using System;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// One Deck card's spiral back, drawn at half size and laid over the card in one stretch (measured 2026-11-17 in
/// DeckSpiralBackBenchTests: a turning bilinear spiral plus the radial dim straight at full size cost about 1.9 ms a
/// card at 1440p; the stretch of a ready picture is the cheap kind, see the Skia stretch trap in AGENTS.md).
/// Nothing is lost at half size: the Loom spiral is only 320 px (FlashLayer.BackSpiralPx), so at full size it was
/// already stretched more than twice over. Bgra8888, <see cref="SKFilterQuality.Low"/>, and nothing allocated per
/// frame: the half-size bitmap, its canvas and the two pictures wrapping shared pixels are made once and again only
/// when the card outgrows them. The card's alpha is applied once, on the stretch, so a fading back fades as one face.
/// </summary>
public sealed class SpiralBackCache : IDisposable
{
    /// <summary>The share of the card's size the back is drawn at. The 320 px spiral spans a 430 x 600 card's
    /// diagonal (738 px) at 295 px here, so it still loses nothing; the stretch back up is one bilinear pass.</summary>
    public const float Scale = 0.4f;
    /// <summary>Sizes round up to this many pixels, so a card reshaping a little reuses the same bitmap.</summary>
    internal const int Step = 32;

    private SKBitmap? _bmp;
    private SKCanvas? _canvas;
    private SKImage? _image;
    private SKBitmap? _spiralSrc;
    private SKImage? _spiralImage;
    // The dim at the centre never turns: drawn once per card size into its own picture, laid on 1:1 every frame.
    private SKBitmap? _dimBmp;
    private SKImage? _dimImage;
    private (float W, float H) _dimFor;
    private readonly SKPaint _copy = new() { IsAntialias = false, FilterQuality = SKFilterQuality.None };
    private readonly SKPaint _blit = new() { IsAntialias = false, FilterQuality = SKFilterQuality.Low };
    private bool _disposed;
    // What the half-size picture holds now: drawn again only when one of these moves (a still back, motion Off,
    // just stretches it again).
    private (double Turn, uint Ground, float W, float H, SKBitmap? Spiral) _drawn;
    private bool _stale = true;

    /// <summary>The spiral's pixels changed in place (a new roll on a flip to the back): draw it again.</summary>
    public void Invalidate() => _stale = true;

    /// <summary>Times the half-size picture was drawn (tests: a still back draws once).</summary>
    internal int Composes { get; private set; }

    /// <summary>The bitmap size for a card of <paramref name="w"/> x <paramref name="h"/> (rounded up to <see cref="Step"/>).</summary>
    internal static (int W, int H) BitmapSize(float w, float h)
        => (Up(w * Scale), Up(h * Scale));

    private static int Up(float v) => Math.Max(Step, (int)Math.Ceiling(v / Step) * Step);

    /// <summary>Times the bitmap was made (tests: a steady card makes it once).</summary>
    internal int Allocations { get; private set; }

    /// <summary>Draws the back over <paramref name="r"/>; false = it could not (the caller draws it directly).</summary>
    public bool Draw(DeckLookDraw look, SKCanvas canvas, SKRect r, SKBitmap spiral, uint ground, double turnRad, byte alpha)
    {
        if (_disposed) return false;
        float cw = r.Width * Scale, ch = r.Height * Scale;
        if (cw < 8 || ch < 8 || cw > 4096 || ch > 4096) return false;
        try
        {
            var (bw, bh) = BitmapSize(r.Width, r.Height);
            // Grow when it is too small, shrink when it is far too big (a card that went from huge to small).
            if (_bmp == null || _bmp.Width < bw || _bmp.Height < bh || _bmp.Width > bw * 2 || _bmp.Height > bh * 2)
            {
                DropBitmap();
                _bmp = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
                _canvas = new SKCanvas(_bmp);
                using (var px = _bmp.PeekPixels()) _image = SKImage.FromPixels(px);
                Allocations++;
                _stale = true;
            }
            if (!ReferenceEquals(spiral, _spiralSrc))
            {
                _spiralImage?.Dispose();
                using (var px = spiral.PeekPixels()) _spiralImage = SKImage.FromPixels(px);
                _spiralSrc = spiral;
                _stale = true;
            }
            if (_canvas == null || _image == null || _spiralImage == null) return false;
            var key = (turnRad, ground, cw, ch, (SKBitmap?)spiral);
            if (_stale || key != _drawn)
            {
                _canvas.Clear(SKColors.Transparent);
                look.Compose(_canvas, new SKRect(0, 0, cw, ch), spiral, ground, turnRad, 255, _spiralImage, dim: false);
                if (_dimImage == null || _dimFor != (cw, ch))
                {
                    _dimImage?.Dispose();
                    _dimBmp ??= new SKBitmap(new SKImageInfo(_bmp!.Width, _bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                    _dimBmp.Erase(SKColors.Transparent);
                    using (var dc = new SKCanvas(_dimBmp)) look.DrawDim(dc, new SKRect(0, 0, cw, ch), 255);
                    using (var px = _dimBmp.PeekPixels()) _dimImage = SKImage.FromPixels(px);
                    _dimFor = (cw, ch);
                }
                _canvas.DrawImage(_dimImage, 0, 0, _copy);
                _canvas.Flush();
                _drawn = key;
                _stale = false;
                Composes++;
            }
            _blit.Color = new SKColor(255, 255, 255, alpha);
            // The WHOLE bitmap, stretched by the same 1 / Scale (a source subset makes Skia take its strict, slower
            // path); the spare rows and columns past the card are clear and fall outside the caller's card clip.
            int save = canvas.Save();
            canvas.ClipRect(r);
            canvas.DrawImage(_image, new SKRect(r.Left, r.Top, r.Left + _bmp!.Width / Scale, r.Top + _bmp.Height / Scale), _blit);
            canvas.RestoreToCount(save);
            return true;
        }
        catch (Exception)
        {
            // Skia only here, no App: the caller draws the back directly instead.
            return false;
        }
    }

    private void DropBitmap()
    {
        _dimImage?.Dispose(); _dimImage = null;
        _dimBmp?.Dispose(); _dimBmp = null;
        _image?.Dispose(); _image = null;
        _canvas?.Dispose(); _canvas = null;
        _bmp?.Dispose(); _bmp = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DropBitmap();
        _spiralImage?.Dispose(); _spiralImage = null;
        _spiralSrc = null;
        _blit.Dispose();
        _copy.Dispose();
    }
}
