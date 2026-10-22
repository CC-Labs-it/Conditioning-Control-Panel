using System;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// How a mandatory video is put on screen (AppSettings.MandatoryVideoFloating): full screen, as
    /// always, or small topmost windows. Pure, so the rule is pinned by tests
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

        /// <summary>The most floating windows that may be up at once (AppSettings.MandatoryVideoFloatingCount).
        /// One trigger opens ONE window; this is only the limit.</summary>
        public const int MaxWindows = 4;

        public const int MinSizePercent = 15, MaxSizePercent = 60, DefaultSizePercent = 33;
        public const int MinSpeedPercent = 10, MaxSpeedPercent = 100, DefaultSpeedPercent = 35;

        /// <summary>No window is narrower than this, whatever the percent (a small monitor).</summary>
        public const int MinWidth = 240;

        public static int ClampCount(int count) => Math.Clamp(count, 1, MaxWindows);
        public static int ClampSizePercent(int percent) => Math.Clamp(percent, MinSizePercent, MaxSizePercent);
        public static int ClampSpeedPercent(int percent) => Math.Clamp(percent, MinSpeedPercent, MaxSpeedPercent);

        /// <summary>The retired small / medium / large choice, carried over once: 25, 33, 50.
        /// Null = not one of the three, nothing to carry.</summary>
        public static int? LegacySizePercent(string? size) => size switch
        {
            "small" => 25,
            "medium" => 33,
            "large" => 50,
            _ => null,
        };

        public enum TriggerDecision { Open, Skip, ReplaceOldest }

        /// <summary>
        /// One trigger, <paramref name="up"/> windows on screen, <paramref name="limit"/> allowed:
        /// under the limit it opens ONE; at the limit an automatic trigger is skipped quietly
        /// (the way full screen refuses while a video plays) and a Test press replaces the oldest.
        /// </summary>
        public static TriggerDecision DecideTrigger(int up, int limit, bool testPress)
            => up < ClampCount(limit) ? TriggerDecision.Open
                : testPress ? TriggerDecision.ReplaceOldest : TriggerDecision.Skip;

        /// <summary>
        /// How many windows one trigger opens with <paramref name="up"/> on screen: one while there
        /// is room, or with "Pop up together" (AppSettings.MandatoryVideoFloatingTogether) every free
        /// slot at the same moment. Zero at the limit (DecideTrigger says what happens then).
        /// </summary>
        public static int WindowsToOpen(int up, int limit, bool together)
        {
            var room = ClampCount(limit) - Math.Max(0, up);
            if (room <= 0) return 0;
            return together ? room : 1;
        }

        /// <summary>
        /// Whether a failed attention check (or the surprise replay after a pass) plays another
        /// video (AppSettings.MandatoryVideoReplayAfterFailedCheck). Turning the replay off is a
        /// comfort choice for an ordinary video only: a strict video and a Lockdown always replay,
        /// the same split as <see cref="ShouldFloat"/>.
        /// </summary>
        public static bool ReplaysAfterCheck(bool replaySetting, bool strict, bool lockdownActive)
            => replaySetting || strict || lockdownActive;

        /// <summary>Bounds of <see cref="JoinDelaySeconds"/>, before the jitter.</summary>
        public const double MinJoinSeconds = 30, MaxJoinSeconds = 600;

        /// <summary>
        /// While floating windows are up with room for one more, the next one comes sooner than a
        /// fresh video would (owner, 2026-10-18: floating videos should overlap now and then, and
        /// more often the higher the frequency). A quarter of the picked gap, held between
        /// <see cref="MinJoinSeconds"/> and <see cref="MaxJoinSeconds"/>, with the scheduler's own
        /// +-20% jitter: 20 an hour joins after about 45 s, 6 after 2.5 min, 1 after 10 min, so a
        /// rare video rarely finds another still playing. Null = a limit of one window, where
        /// nothing can join, or "Pop up together", where the run opened full and nothing joins it:
        /// the ordinary gap applies. <paramref name="unit"/> is a 0..1 draw.
        /// </summary>
        public static double? JoinDelaySeconds(int videosPerHour, int limit, double unit, bool together = false)
        {
            if (together || ClampCount(limit) < 2) return null;
            var gap = 3600.0 / Math.Max(1, videosPerHour);
            return Math.Clamp(gap / 4, MinJoinSeconds, MaxJoinSeconds) * (0.8 + Math.Clamp(unit, 0, 1) * 0.4);
        }

        /// <summary>Attention checks belong to a full-screen video only. A floating one has no
        /// targets, no mini-game, no verdict and no replay, on either engine.</summary>
        public static bool AttentionChecksApply(bool attentionSetting, bool floating) => attentionSetting && !floating;

        /// <summary>
        /// A floating window on a <paramref name="areaW"/> x <paramref name="areaH"/> work area, in
        /// that area's DIPs: 16:9, <paramref name="sizePercent"/> of the width (never larger than
        /// the area), at a random spot inside the flash images' edge padding and always wholly on
        /// that screen. Windows may overlap the ones already up (<paramref name="taken"/>, same
        /// DIPs); the only thing avoided is landing exactly on top of one, which would hide it.
        /// </summary>
        public static (int X, int Y, int W, int H) FloatingRect(int areaW, int areaH, Random random, int sizePercent,
            System.Collections.Generic.IReadOnlyList<(int X, int Y, int W, int H)>? taken = null)
        {
            areaW = Math.Max(1, areaW);
            areaH = Math.Max(1, areaH);
            var w = Math.Min(areaW, Math.Max(MinWidth, areaW * ClampSizePercent(sizePercent) / 100));
            var h = w * 9 / 16;
            if (h > areaH) { h = areaH; w = Math.Min(areaW, h * 16 / 9); }
            var (minX, minY, maxX, maxY) = Flash.FlashPlacement.SpawnBounds(areaW, areaH, w, h);   // WPF FlashService.SpawnBounds
            var x = Math.Clamp(random.Next(minX, maxX), 0, areaW - w);
            var y = Math.Clamp(random.Next(minY, maxY), 0, areaH - h);
            if (taken != null)
                for (var i = 0; i < 8 && OnTopOf(x, y, taken); i++)
                {
                    // A small step is enough: toward whichever side has the room.
                    x = x + PileOffset <= areaW - w ? x + PileOffset : Math.Max(0, x - PileOffset);
                    y = y + PileOffset <= areaH - h ? y + PileOffset : Math.Max(0, y - PileOffset);
                }
            return (x, y, w, h);
        }

        /// <summary>Closer than this on both axes reads as one window.</summary>
        public const int PileOffset = 36;

        private static bool OnTopOf(int x, int y, System.Collections.Generic.IReadOnlyList<(int X, int Y, int W, int H)> taken)
        {
            foreach (var t in taken)
                if (Math.Abs(t.X - x) < PileOffset && Math.Abs(t.Y - y) < PileOffset) return true;
            return false;
        }
    }
}
