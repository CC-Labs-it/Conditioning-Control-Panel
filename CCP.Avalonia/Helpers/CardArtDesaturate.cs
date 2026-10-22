using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>A small immutable grey twin, cached for the bitmap's lifetime as in WPF ArtDesaturate.</summary>
internal static class CardArtDesaturate
{
    private static readonly ConditionalWeakTable<IImageBrushSource, Bitmap> Cache = new();

    internal static Bitmap? Of(IImageBrushSource? source)
    {
        if (source is not Bitmap bitmap) return null;
        if (Cache.TryGetValue(source, out var cached)) return cached;
        try
        {
            int sw = bitmap.PixelSize.Width, sh = bitmap.PixelSize.Height;
            if (sw <= 0 || sh <= 0) return null;
            // Scale before touching pixels, as WPF ArtDesaturate does; no PNG encode/decode round trip.
            double scale = Math.Min(1, 320.0 / Math.Max(sw, sh));
            int w = Math.Max(1, (int)Math.Round(sw * scale));
            int h = Math.Max(1, (int)Math.Round(sh * scale));
            // Skia's ResizeBitmap only accepts immutable (decoded) bitmaps; a WriteableBitmap is resampled below instead.
            using var scaled = scale < 1 && bitmap is not WriteableBitmap
                ? bitmap.CreateScaledBitmap(new PixelSize(w, h), BitmapInterpolationMode.HighQuality) : null;
            var src = scaled ?? bitmap;
            using var staging = new WriteableBitmap(src.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var frame = staging.Lock();
            src.CopyPixels(frame);   // transcodes Rgba/Unpremul sources to Bgra8888 Premul
            using var full = new SKBitmap();
            full.InstallPixels(new SKImageInfo(src.PixelSize.Width, src.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul),
                frame.Address, frame.RowBytes);
            using var small = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(small))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
                canvas.DrawBitmap(full, new SKRect(0, 0, w, h), paint);
            var bytes = small.Bytes;
            CardMutePixels.ToGrey(bytes);
            var grey = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = grey.Lock())
                for (int y = 0; y < h; y++) Marshal.Copy(bytes, y * w * 4, buffer.Address + y * buffer.RowBytes, w * 4);
            Cache.Add(source, grey);
            return grey;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "split art desaturate"); return null; }
    }
}
