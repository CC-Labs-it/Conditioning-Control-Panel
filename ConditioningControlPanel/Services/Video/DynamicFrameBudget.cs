using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>The pure rules of the moving wallpaper: how many frames of a gif it keeps, how far a tile
    /// advances, and when a window hides a monitor. Apart so tests never need a GPU.</summary>
    internal static class DynamicFrameBudget
    {
        /// <summary>GPU memory one build's moving frames may take, shared by every monitor's collage. A rotation holds two builds for the
        /// length of the crossfade, so double this at the peak.</summary>
        public const long Bytes = 512L * 1024 * 1024;

        /// <summary>Most frames kept per tile. A longer gif keeps every Nth frame and holds each for longer.</summary>
        public const int MaxFramesKept = 120;

        /// <summary>A longer source stays a still: every frame has to be decoded even when few are kept.</summary>
        public const int MaxSourceFrames = 900;

        /// <summary>Fill one monitor with a collage planned for another size (only between a display change and
        /// its rebuild; every monitor normally gets its own): one uniform scale, centred, the overflow cropped. Scaling each axis on its own squashed a landscape collage onto a portrait monitor
        /// (ccp-bugs #1544); this keeps every picture's shape on any monitor.</summary>
        public static (float Scale, float OffsetX, float OffsetY) CoverFit(float monW, float monH, float refW, float refH)
        {
            if (monW <= 0 || monH <= 0 || refW <= 0 || refH <= 0) return (1f, 0f, 0f);
            float k = Math.Max(monW / refW, monH / refH);
            return (k, (monW - refW * k) / 2f, (monH - refH * k) / 2f);
        }

        /// <summary>Which planned monitor collage belongs to the monitor at this rect: the one planned for
        /// exactly this rect, else the one it overlaps most (a display change since the build; the restart
        /// that follows builds new ones), else the first. -1 only when nothing was planned.</summary>
        public static int MatchMonitor(System.Collections.Generic.IReadOnlyList<(int X, int Y, int W, int H)> planned,
            int x, int y, int w, int h)
        {
            if (planned == null || planned.Count == 0) return -1;
            int best = 0;
            long bestArea = -1;
            for (int i = 0; i < planned.Count; i++)
            {
                var p = planned[i];
                if (p.X == x && p.Y == y && p.W == w && p.H == h) return i;
                long ow = Math.Min(p.X + p.W, x + w) - Math.Max(p.X, x);
                long oh = Math.Min(p.Y + p.H, y + h) - Math.Max(p.Y, y);
                long area = ow > 0 && oh > 0 ? ow * oh : 0;
                if (area > bestArea) { bestArea = area; best = i; }
            }
            return best;
        }

        /// <summary>Keep every Nth frame so that at most <see cref="MaxFramesKept"/> remain.</summary>
        public static int Stride(int frameCount)
            => frameCount <= MaxFramesKept ? 1 : (int)Math.Ceiling(frameCount / (double)MaxFramesKept);

        /// <summary>Bytes a tile of this bake size and source frame count will take.</summary>
        public static long Cost(int bakeW, int bakeH, int frameCount)
        {
            int kept = (int)Math.Ceiling(frameCount / (double)Stride(frameCount));
            return bakeW * (long)bakeH * 4 * kept;
        }

        /// <summary>May this tile move? Short enough to decode, and within what the collage has left.</summary>
        public static bool MayMove(int frameCount, long cost, long budgetLeft)
            => frameCount > 1 && frameCount <= MaxSourceFrames && cost <= budgetLeft;

        /// <summary>Move a clock on by <paramref name="dtMs"/>. Frame delays divide by the Gentle speed, as
        /// GentleVisuals.ScaleDelayMs does. True when the visible frame changed.</summary>
        public static bool Advance(int[] delays, ref int cur, ref double clock, long dtMs, double speed)
        {
            int count = delays.Length;
            if (count < 2) return false;
            double rate = speed <= 0 ? 1 : speed;
            int before = cur;
            clock += dtMs;
            int guard = 0;
            while (clock >= delays[cur] / rate && guard++ < count)
            {
                clock -= delays[cur] / rate;
                cur = (cur + 1) % count;
            }
            return cur != before;
        }

        /// <summary>See <see cref="Advance(int[], ref int, ref double, long, double)"/>.</summary>
        public static bool Advance(DynamicWallpaperService.Tile t, long dtMs, double speed)
            => Advance(t.Delays, ref t.Cur, ref t.Clock, dtMs, speed);

        /// <summary>Does a window hide this whole monitor? A maximised window stops at the taskbar, so being
        /// zoomed on that monitor counts; anything else has to span it.</summary>
        public static bool Covers(int left, int top, int right, int bottom, bool zoomedOnThisMonitor,
            int monX, int monY, int monW, int monH)
            => zoomedOnThisMonitor
               || (left <= monX && top <= monY && right >= monX + monW && bottom >= monY + monH);
    }
}
