// The avatar tube's X11 input shape: which window pixels take the mouse.
//
// WPF's tube is a layered window (AllowsTransparency, AvatarTubeWindow.xaml:1-12): Windows gives the
// mouse to every pixel painted with alpha above zero and passes the rest through to whatever is
// underneath - WPF TubeArtAlpha.cs:8-18 relies on exactly that, scrubbing alpha <= 8 so faint haze
// in mod art stops swallowing clicks (#1400). An X11 window takes clicks on its whole rectangle,
// so a 780x1080 transparent tube was an invisible box blocking the desktop beside CCP (owner,
// Linux beta WSLg run). The X11 equivalent is an input shape (XFixes ShapeInput) covering only
// what is painted: the tube art's own alpha (cells above TubeArtAlpha's floor) plus the bounds of
// every visible element drawn over it (avatar, bubble, title plate, chat input, cards).
//
// Everything in here is pure and window-free so tests can pin the region without an X server.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>A rectangle in mask cells, half-open: [X0, X1) x [Y0, Y1).</summary>
    internal readonly record struct MaskRun(int X0, int Y0, int X1, int Y1);

    /// <summary>The painted part of a piece of tube art, as cell runs at reduced resolution.</summary>
    internal sealed class TubeArtMask
    {
        /// <summary>WPF TubeArtAlpha.Floor: alpha at or below this is haze, not art.</summary>
        internal const byte Floor = 8;

        /// <summary>Longest mask side in cells. 384 cells over the 780-DIP canvas is ~2 DIP a cell:
        /// finer than any hit a finger or cursor makes, coarse enough to stay a few hundred rects.</summary>
        internal const int MaxCells = 384;

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<MaskRun> Runs { get; }

        internal TubeArtMask(int width, int height, IReadOnlyList<MaskRun> runs)
        {
            Width = width; Height = height; Runs = runs;
        }

        /// <summary>Row runs of alpha &gt; <paramref name="floor"/>, merged downwards while a run
        /// repeats unchanged on the next row. <paramref name="alphaOffset"/> is 3 for BGRA/RGBA.</summary>
        internal static TubeArtMask FromPixels(byte[] px, int width, int height, int stride, byte floor = Floor, int alphaOffset = 3)
        {
            var done = new List<MaskRun>();
            // run (x0,x1) -> first row it was seen in, for the runs still open on the previous row
            var open = new Dictionary<(int, int), int>();
            var row = new List<(int, int)>();
            for (int y = 0; y <= height; y++)
            {
                row.Clear();
                if (y < height)
                {
                    int start = -1, o = y * stride + alphaOffset;
                    for (int x = 0; x < width; x++, o += 4)
                    {
                        bool on = px[o] > floor;
                        if (on && start < 0) start = x;
                        else if (!on && start >= 0) { row.Add((start, x)); start = -1; }
                    }
                    if (start >= 0) row.Add((start, width));
                }
                var next = new Dictionary<(int, int), int>(row.Count);
                foreach (var r in row) next[r] = open.TryGetValue(r, out var y0) ? y0 : y;
                foreach (var (r, y0) in open)
                    if (!next.ContainsKey(r)) done.Add(new MaskRun(r.Item1, y0, r.Item2, y));
                open = next;
            }
            return new TubeArtMask(width, height, done);
        }

        private static readonly ConditionalWeakTable<Bitmap, TubeArtMask> Cache = new();

        /// <summary>The mask of <paramref name="art"/>, computed once per bitmap. Null when the
        /// pixels cannot be read; callers then treat the whole picture as painted.</summary>
        internal static TubeArtMask? For(Bitmap art)
        {
            if (Cache.TryGetValue(art, out var known)) return known;
            try
            {
                var size = art.PixelSize;
                if (size.Width <= 0 || size.Height <= 0) return null;
                double k = Math.Min(1, (double)MaxCells / Math.Max(size.Width, size.Height));
                var cells = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k)));
                // Skia resamples only immutable (decoded) bitmaps; a WriteableBitmap is read at full size.
                using var scaled = k < 1 && art is not WriteableBitmap
                    ? art.CreateScaledBitmap(cells, BitmapInterpolationMode.MediumQuality) : null;
                var src = scaled ?? art;
                var ps = src.PixelSize;
                // Bgra8888 staging transcodes whatever format the decoder chose (CardArtDesaturate.cs).
                using var staging = new WriteableBitmap(ps, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                byte[] px;
                int stride;
                using (var fb = staging.Lock())
                {
                    src.CopyPixels(fb);
                    stride = fb.RowBytes;
                    px = new byte[stride * ps.Height];
                    System.Runtime.InteropServices.Marshal.Copy(fb.Address, px, 0, px.Length);
                }
                var mask = FromPixels(px, ps.Width, ps.Height, stride);
                Cache.AddOrUpdate(art, mask);
                return mask;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TubeArtMask: tube art pixels unreadable; the whole picture takes input");
                return null;
            }
        }
    }

    internal static class TubeInputShape
    {
        /// <summary>The mask's runs mapped onto <paramref name="dest"/> (the picture's drawn rect in
        /// window px), rounded OUTWARDS so a painted edge on a fractional pixel still takes the click.</summary>
        internal static void AddArt(List<PixelRect> into, TubeArtMask mask, Rect dest)
        {
            if (mask.Width <= 0 || mask.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return;
            double sx = dest.Width / mask.Width, sy = dest.Height / mask.Height;
            foreach (var r in mask.Runs)
                AddBox(into, new Rect(dest.X + r.X0 * sx, dest.Y + r.Y0 * sy, (r.X1 - r.X0) * sx, (r.Y1 - r.Y0) * sy));
        }

        /// <summary>A control's box in window px, rounded outwards.</summary>
        internal static void AddBox(List<PixelRect> into, Rect box)
        {
            if (!(box.Width > 0) || !(box.Height > 0)) return;
            int x0 = (int)Math.Floor(box.X), y0 = (int)Math.Floor(box.Y);
            int x1 = (int)Math.Ceiling(box.Right), y1 = (int)Math.Ceiling(box.Bottom);
            if (x1 > x0 && y1 > y0) into.Add(new PixelRect(x0, y0, x1 - x0, y1 - y0));
        }

        /// <summary>Where a Stretch=Uniform picture of <paramref name="source"/> px lands inside
        /// <paramref name="slot"/> (centred, as ImgTubeFrame is).</summary>
        internal static Rect UniformFit(Size source, Rect slot)
        {
            if (source.Width <= 0 || source.Height <= 0) return slot;
            double k = Math.Min(slot.Width / source.Width, slot.Height / source.Height);
            double w = source.Width * k, h = source.Height * k;
            return new Rect(slot.X + (slot.Width - w) / 2, slot.Y + (slot.Height - h) / 2, w, h);
        }

        /// <summary>Every rect cut to <paramref name="clip"/> (the attached dock cut, #1847) and to the
        /// window; empties dropped. Overlaps are fine: XFixesCreateRegion unions them.</summary>
        internal static PixelRect[] Finish(List<PixelRect> rects, PixelRect window, PixelRect? clip)
        {
            var bound = clip is { } c ? Intersect(window, c) : window;
            var result = new List<PixelRect>(rects.Count);
            foreach (var r in rects)
            {
                var cut = Intersect(r, bound);
                if (cut.Width > 0 && cut.Height > 0) result.Add(cut);
            }
            return result.ToArray();
        }

        private static PixelRect Intersect(PixelRect a, PixelRect b)
        {
            int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
            int x1 = Math.Min(a.Right, b.Right), y1 = Math.Min(a.Bottom, b.Bottom);
            return x1 > x0 && y1 > y0 ? new PixelRect(x0, y0, x1 - x0, y1 - y0) : default;
        }

        /// <summary>True when (x, y) - window px - takes input under <paramref name="rects"/>.</summary>
        internal static bool Hits(IReadOnlyList<PixelRect> rects, int x, int y)
        {
            foreach (var r in rects)
                if (x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom) return true;
            return false;
        }

        internal static bool SameRects(PixelRect[]? a, PixelRect[]? b)
        {
            if (a is null || b is null) return a is null && b is null;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
