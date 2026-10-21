using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Services;
using SkiaSharp;

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
            using var png = new MemoryStream();
            bitmap.Save(png);
            using var decoded = SKBitmap.Decode(png.ToArray());
            if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0) return null;
            double scale = Math.Min(1, 320.0 / Math.Max(decoded.Width, decoded.Height));
            int w = Math.Max(1, (int)Math.Round(decoded.Width * scale));
            int h = Math.Max(1, (int)Math.Round(decoded.Height * scale));
            using var small = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(small))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
                canvas.DrawBitmap(decoded, new SKRect(0, 0, w, h), paint);
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
