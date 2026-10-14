using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// This head's PNG codec for Core's board (<see cref="BoardPicture.PngDecoder"/>): WPF imaging,
    /// any thread, exactly what <see cref="BoardPicture.Decode"/> did before it moved to CCP.Core.
    /// </summary>
    public static class BoardPng
    {
        public static void Install() => BoardPicture.PngDecoder ??= Decode;

        public static (int[] Pixels, int Width, int Height)? Decode(byte[] png)
        {
            using var ms = new MemoryStream(png, writable: false);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            BitmapSource src = decoder.Frames[0];
            if (src.PixelWidth <= 0 || src.PixelHeight <= 0) return null;
            // Checked before the pixel copy, so an oversized strip never allocates.
            if (!BoardPicture.FitsStrip(src.PixelWidth, src.PixelHeight)) return null;
            if (src.Format != PixelFormats.Bgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new int[w * h];
            src.CopyPixels(px, w * 4, 0);
            return (px, w, h);
        }
    }
}
