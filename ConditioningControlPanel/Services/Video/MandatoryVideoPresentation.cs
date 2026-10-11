using System;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// How a mandatory video is put on screen (AppSettings.MandatoryVideoFloating): full screen, as
    /// always, or one small topmost window. Pure, so the rule is pinned by tests
    /// (MandatoryVideoFloatingTests). The player, the timers and every stop rule are the same in both.
    /// </summary>
    internal static class MandatoryVideoPresentation
    {
        /// <summary>
        /// Floating is a comfort option for an ordinary video only. Anything that covers the screen
        /// ON PURPOSE keeps covering it: a strict video (Strict Lock, Lock Card, Possession, a
        /// strict override from a leash or a session), a Lockdown, and a video inside a Chaos run
        /// (the run draws its own layer over the full-screen clip). The strict window's own Escape
        /// pause door also needs keyboard focus, which a floating window never takes.
        /// </summary>
        public static bool ShouldFloat(bool floatingSetting, bool strict, bool lockdownActive, bool chaosRunning)
            => floatingSetting && !strict && !lockdownActive && !chaosRunning;

        /// <summary>
        /// The floating window on a <paramref name="areaW"/> x <paramref name="areaH"/> work area, in
        /// that area's DIPs: 16:9, a third of the width (never under 480 wide, so the grace card and
        /// a target fit; never larger than the area), at a random spot inside the flash images' edge
        /// padding and always wholly on that screen. Same rule as the port's FloatingRect.
        /// </summary>
        public static (int X, int Y, int W, int H) FloatingRect(int areaW, int areaH, Random random)
            => FloatingRect(areaW, areaH, random, SizeDivisor(SizeMedium), PrimaryMinWidth, null);

        public const string SizeSmall = "small", SizeMedium = "medium", SizeLarge = "large";

        /// <summary>The most floating windows one event may open (AppSettings.MandatoryVideoFloatingCount).</summary>
        public const int MaxWindows = 4;

        /// <summary>The primary never goes under this (grace card + one target); a companion carries
        /// neither, so it may be smaller.</summary>
        public const int PrimaryMinWidth = 480, CompanionMinWidth = 320;

        public static int ClampCount(int count) => Math.Clamp(count, 1, MaxWindows);

        public static string NormalizeSize(string? size) => size is SizeSmall or SizeLarge ? size : SizeMedium;

        /// <summary>The window is the work area's width over this: small a quarter, medium a third
        /// (the first cut's size), large a half.</summary>
        public static int SizeDivisor(string? size) => NormalizeSize(size) switch { SizeSmall => 4, SizeLarge => 2, _ => 3 };

        /// <summary>
        /// The same rule at any size, and spread: with windows already up (<paramref name="taken"/>,
        /// same DIPs) it tries a few spots and keeps the one that covers them least, so a new window
        /// does not start stacked on another.
        /// </summary>
        public static (int X, int Y, int W, int H) FloatingRect(int areaW, int areaH, Random random, int divisor, int minWidth,
            System.Collections.Generic.IReadOnlyList<(int X, int Y, int W, int H)>? taken)
        {
            areaW = Math.Max(1, areaW);
            areaH = Math.Max(1, areaH);
            var w = Math.Min(areaW, Math.Max(minWidth, areaW / Math.Max(1, divisor)));
            var h = w * 9 / 16;
            if (h > areaH) { h = areaH; w = Math.Min(areaW, h * 16 / 9); }
            var (minX, minY, maxX, maxY) = FlashService.SpawnBounds(areaW, areaH, w, h);
            var tries = taken is { Count: > 0 } ? 16 : 1;
            (int X, int Y, int W, int H) best = default;
            var bestCover = long.MaxValue;
            for (var i = 0; i < tries && bestCover > 0; i++)
            {
                var x = Math.Clamp(random.Next(minX, maxX), 0, areaW - w);
                var y = Math.Clamp(random.Next(minY, maxY), 0, areaH - h);
                long cover = 0;
                if (taken != null)
                    foreach (var t in taken)
                        cover += (long)Math.Max(0, Math.Min(x + w, t.X + t.W) - Math.Max(x, t.X))
                                 * Math.Max(0, Math.Min(y + h, t.Y + t.H) - Math.Max(y, t.Y));
                if (cover < bestCover) { bestCover = cover; best = (x, y, w, h); }
            }
            return best;
        }

        /// <summary>Attention targets shrink with the window (a third, like the port) so one fits
        /// inside it; full screen keeps the player's own size.</summary>
        public static int AttentionSize(int size, bool floating) => floating ? Math.Max(16, size / 3) : size;
    }
}
