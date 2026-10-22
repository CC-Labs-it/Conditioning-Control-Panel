using System;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.AvatarTubeLayout
{
    /// <summary>
    /// Wipes the invisible haze off tube art before it reaches the tube window.
    ///
    /// <para>The tube is a layered window: every pixel it paints with an alpha above zero takes the
    /// mouse, however faint. A cut-out tube picture can carry a background that looks empty and is
    /// not (the Locked mod's detached tube has alpha 1 to 6 across a full-height column on the right
    /// and a block at the top-left). Nobody can see it, the window still owns the mouse there, and
    /// while detached the tube frame is a drag handle, so the empty space next to the companion
    /// dragged it and swallowed clicks meant for the window underneath (#1400).</para>
    ///
    /// <para>Anything at or below <see cref="Floor"/> (about 3 percent) goes to exactly zero. The
    /// picture is otherwise untouched, and clean art comes back as the same instance.</para>
    /// </summary>
    public static class TubeArtAlpha
    {
        /// <summary>The highest alpha that counts as haze. Real glass edges fade through this band
        /// in a pixel or two, which no eye picks out at the size the tube is drawn.</summary>
        public const byte Floor = 8;

        private static readonly ConditionalWeakTable<ImageSource, ImageSource> _scrubbed = new();

        /// <summary>
        /// Zeroes every premultiplied BGRA pixel whose alpha is above zero and at or below
        /// <paramref name="floor"/>. Returns how many pixels changed.
        /// </summary>
        public static int Clean(byte[] pbgra, byte floor = Floor)
        {
            if (pbgra == null) return 0;
            int changed = 0;
            for (int i = 3; i < pbgra.Length; i += 4)
            {
                byte a = pbgra[i];
                if (a == 0 || a > floor) continue;
                pbgra[i - 3] = 0; pbgra[i - 2] = 0; pbgra[i - 1] = 0; pbgra[i] = 0;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// The same picture with its haze gone. Returns <paramref name="art"/> itself when there is
        /// nothing to clean, when it is not a bitmap, or when anything goes wrong: tube art never
        /// gets to fail over a cosmetic pass.
        /// </summary>
        public static ImageSource? Scrub(ImageSource? art)
        {
            if (art is not BitmapSource source) return art;
            if (_scrubbed.TryGetValue(art, out var known)) return known;

            ImageSource result = art;
            try
            {
                BitmapSource pbgra = source.Format == PixelFormats.Pbgra32
                    ? source
                    : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                int width = pbgra.PixelWidth, height = pbgra.PixelHeight;
                if (width > 0 && height > 0)
                {
                    int stride = width * 4;
                    var pixels = new byte[stride * height];
                    pbgra.CopyPixels(pixels, stride, 0);
                    if (Clean(pixels) > 0)
                    {
                        var clean = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
                            PixelFormats.Pbgra32, null, pixels, stride);
                        clean.Freeze();
                        result = clean;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Tube art: haze pass skipped ({Error})", ex.Message);
                result = art;
            }

            try { _scrubbed.Add(art, result); } catch (ArgumentException) { }
            return result;
        }
    }
}
