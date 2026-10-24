using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>Apply the shared WPF haze floor before tube art reaches the transparent window.</summary>
internal static class TubeArtAlpha
{
    private static readonly ConditionalWeakTable<Bitmap, Bitmap> Scrubbed = new();

    internal static IImage? Scrub(IImage? art)
    {
        if (art is not Bitmap source) return art;
        return Scrubbed.GetValue(source, CleanBitmap);
    }

    private static Bitmap CleanBitmap(Bitmap source)
    {
        try
        {
            // Normalize to premultiplied BGRA, including mod images decoded in other formats.
            using var stream = new MemoryStream();
            source.Save(stream);
            stream.Position = 0;
            using var bitmap = SKBitmap.Decode(stream, new SKImageInfo(source.PixelSize.Width,
                source.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (bitmap == null) return source;
            var pixels = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            if (AvatarTubeLayout.TubeArtAlpha.Clean(pixels) == 0) return source;
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            // Bitmap copies the pixels; no mutable SKBitmap crosses a thread boundary.
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, bitmap.GetPixels(),
                source.PixelSize, source.Dpi, bitmap.RowBytes);
        }
        catch (Exception ex)
        {
            Log.Debug("Tube art: haze pass skipped ({Error})", ex.Message);
            return source;
        }
    }
}
