using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The section pills' motion (nav polish wave 7, "they should POP"), shared by both heads:
    /// hover lifts and grows a pill and wiggles its glyph, choosing throws a spark burst, the lit
    /// pill catches a sheen every 9 s. Full = all of it, Reduced = the lift only, Off = nothing.
    /// Moved from WPF SectionTabStrip.Fx.cs NavStripFxRules with the numbers unchanged; that class
    /// now forwards here.
    /// </summary>
    public static class NavStripMotion
    {
        public const double HoverLiftPx = DepthLaw.HoverLiftPx;
        public const double HoverScale = 1.035;
        public const int HoverInMs = 120, HoverOutMs = 160;
        public const double WiggleDegrees = 8;
        public const int WiggleMs = 280;
        public const int SheenEveryMs = 9000, SheenMs = 650;
        public const double SheenWidth = 40, SheenOpacity = 0.22;
        public const int BurstSparks = 18;
        /// <summary>How far the spark layer spills past the track on every side.</summary>
        public const double SparkBleed = 12;

        public static double LiftFor(MotionLevel level) => level == MotionLevel.Off ? 0 : HoverLiftPx;
        public static double ScaleFor(MotionLevel level) => level == MotionLevel.Full ? HoverScale : 1.0;
        public static bool Wiggles(MotionLevel level) => level == MotionLevel.Full;

        /// <summary>The sheen clock runs only on Full, on a tier allowing ambient motion, while the
        /// strip is on screen and the window's chrome loops are allowed.</summary>
        public static bool SheenRuns(MotionLevel level, bool tierAllowsAmbient, bool visible, bool chromeAllows) =>
            level == MotionLevel.Full && tierAllowsAmbient && visible && chromeAllows;
    }
}
