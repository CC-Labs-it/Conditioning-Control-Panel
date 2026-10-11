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
        {
            areaW = Math.Max(1, areaW);
            areaH = Math.Max(1, areaH);
            var w = Math.Min(areaW, Math.Max(480, areaW / 3));
            var h = w * 9 / 16;
            if (h > areaH) { h = areaH; w = Math.Min(areaW, h * 16 / 9); }
            var (minX, minY, maxX, maxY) = FlashService.SpawnBounds(areaW, areaH, w, h);
            var x = Math.Clamp(random.Next(minX, maxX), 0, areaW - w);
            var y = Math.Clamp(random.Next(minY, maxY), 0, areaH - h);
            return (x, y, w, h);
        }

        /// <summary>Attention targets shrink with the window (a third, like the port) so one fits
        /// inside it; full screen keeps the player's own size.</summary>
        public static int AttentionSize(int size, bool floating) => floating ? Math.Max(16, size / 3) : size;
    }
}
