using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/CardMute.cs (ArtDesaturate): a greyscale twin of
    /// a card's art, cached per source, capped at <see cref="MaxEdge"/> px (the card never shows more).
    /// Null for non-bitmap art or a pixel format other than Bgra/Rgba 8888 (Avalonia has no
    /// FormatConvertedBitmap); the opacity dim then says "off" on its own, as in WPF.
    /// </summary>
    internal static class ArtDesaturate
    {
        public const int MaxEdge = 320;

        private static readonly ConditionalWeakTable<Bitmap, Bitmap> Cache = new();

        public static Bitmap? Of(IImageBrushSource? source)
        {
            if (source is not Bitmap bmp) return null;
            try
            {
                if (Cache.TryGetValue(bmp, out var hit)) return hit;
                var grey = Build(bmp);
                if (grey == null) return null;
                Cache.AddOrUpdate(bmp, grey);
                return grey;
            }
            catch (Exception ex)
            {
                Log.Debug("ArtDesaturate: {E}", ex.Message);
                return null;
            }
        }

        private static Bitmap? Build(Bitmap bmp)
        {
            var fmt = bmp.Format;
            bool bgra = fmt == PixelFormat.Bgra8888;
            if (!bgra && fmt != PixelFormat.Rgba8888) return null;
            int sw = bmp.PixelSize.Width, sh = bmp.PixelSize.Height;
            if (sw <= 0 || sh <= 0) return null;

            int longest = Math.Max(sw, sh);
            using var scaled = longest > MaxEdge
                ? bmp.CreateScaledBitmap(new PixelSize(Math.Max(1, sw * MaxEdge / longest), Math.Max(1, sh * MaxEdge / longest)))
                : null;
            var src = scaled ?? bmp;
            // The scaled copy may not keep the source's channel order: read the format it has.
            fmt = src.Format;
            bgra = fmt == PixelFormat.Bgra8888;
            if (!bgra && fmt != PixelFormat.Rgba8888) return null;
            int w = src.PixelSize.Width, h = src.PixelSize.Height, stride = w * 4;
            var pixels = new byte[stride * h];
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                src.CopyPixels(new PixelRect(0, 0, w, h), pin.AddrOfPinnedObject(), pixels.Length, stride);
                ToGrey(pixels, bgra);
                return new Bitmap(fmt.Value, src.AlphaFormat ?? AlphaFormat.Premul, pin.AddrOfPinnedObject(),
                    new PixelSize(w, h), new Vector(96, 96), stride);
            }
            finally { pin.Free(); }
        }

        /// <summary>WPF ToGrey: 0.114 B + 0.587 G + 0.299 R in 16.16 fixed point. A weighted sum of
        /// premultiplied channels stays premultiplied, so alpha is untouched.</summary>
        internal static void ToGrey(byte[] px, bool bgra)
        {
            int wb = bgra ? 7471 : 19595, wr = bgra ? 19595 : 7471;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                int luma = (px[i] * wb + px[i + 1] * 38470 + px[i + 2] * wr + 32768) >> 16;
                px[i] = px[i + 1] = px[i + 2] = (byte)luma;
            }
        }
    }
}
