using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// A greyscale twin of a tile's art, built once per source and cached for its lifetime.
    ///
    /// <para>Not a pixel shader: WPF's ShaderEffect needs a compiled .ps and a build step this
    /// repo does not have, and the cheap alternative is at least as good here. The twin is
    /// downscaled to <see cref="MaxEdge"/> before conversion (a tile is ~150px on the wall, the
    /// source art is 1024), so the conversion is a few thousand pixels, alpha survives (the
    /// vault and the tease tile art carry it; a Gray8 FormatConvertedBitmap would drop it), and
    /// the result is a frozen bitmap the card can paint under its colour layer for free.</para>
    /// </summary>
    public static class ArtDesaturate
    {
        /// <summary>Longest edge of the grey twin. Larger than a wall tile, smaller than the
        /// source, so the twin never looks soft on the tile and never costs a full decode.</summary>
        public const int MaxEdge = 320;

        private static readonly ConditionalWeakTable<ImageSource, ImageSource> Cache = new();

        /// <summary>The grey twin, or null when the source is not a bitmap (a DrawingImage, say)
        /// or cannot be read - the caller then falls back to the opacity dim alone.</summary>
        public static ImageSource? Of(ImageSource? source)
        {
            if (source is not BitmapSource bmp) return null;
            try
            {
                if (Cache.TryGetValue(source, out var hit)) return hit;
                var grey = Build(bmp);
                if (grey == null) return null;
                Cache.AddOrUpdate(source, grey);
                return grey;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "art desaturate");
                return null;
            }
        }

        private static ImageSource? Build(BitmapSource bmp)
        {
            if (bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return null;

            BitmapSource src = bmp;
            int longest = Math.Max(bmp.PixelWidth, bmp.PixelHeight);
            if (longest > MaxEdge)
            {
                double scale = (double)MaxEdge / longest;
                src = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
            }
            if (src.Format != PixelFormats.Bgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);
            ToGrey(pixels);

            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// In-place Rec.601 luma over a BGRA32 buffer. Alpha is untouched, and every colour
        /// channel lands on the same value, so the pixel is grey by construction. Kept pure so
        /// the maths can be pinned by a test with no bitmap in sight.
        /// </summary>
        internal static void ToGrey(byte[] bgra) => CardMutePixels.ToGrey(bgra);
    }
}
