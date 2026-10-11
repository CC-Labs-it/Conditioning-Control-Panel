using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The depth pass's numbers (nav polish wave 10, 2026-10-06), shared by both heads: one lamp
    /// top-left, three heights (Raised / Floating / Sunken), "on is pressed in". Moved from WPF
    /// Controls/Depth/DepthRules.cs with the numbers unchanged; WPF DepthRules now reads them from
    /// here and keeps only its Color wrappers. Colours are 0xAARRGGBB, mixed by NavStripPaint.
    /// The brushes that paint these numbers are Depth.xaml on each head.
    /// </summary>
    public static class DepthLaw
    {
        public const double RaisedPx = 3.0, HoverLiftPx = 2.0, PressTravelPx = 2.0, ActiveSinkPx = 1.0;
        public const double FloatPx = 10.0, StartPx = 5.0, WellPx = 9.0, RailShadowPx = 6.0, LedgeUpPx = 14.0;
        public const double HighlightAlpha = 0.26, ShadeAlpha = 0.50, SheenAlpha = 0.09, ShadowAlpha = 0.62;
        public const double WellTopAlpha = 0.65, WellLeftAlpha = 0.35, WellFootAlpha = 0.07;
        public const double FloatAlpha = 0.40, LedgeUpAlpha = 0.70, PressedShadeAlpha = 0.60;
        public const int PressMs = 90, ReleaseMs = 140, HoverMs = 120;
        public const double ReleaseOvershootPx = 1.0, TiltDegrees = 1.2;
        /// <summary>How far a shadow is pulled from deep ink toward the section hue.</summary>
        public const double ShadowHuePull = 0.28;
        public const uint NeutralShadowInk = 0xFF060210;

        /// <summary>Motion-scaled ms: Full as authored, Reduced halves, Off is instant (WPF NavRailRules.Ms).</summary>
        public static int Ms(int fullMs, MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => fullMs / 2,
            _ => fullMs,
        };

        /// <summary>A section shadow: deep ink pulled 28% toward the hue, at an alpha.</summary>
        public static uint ShadowColor(uint hue, double alpha = ShadowAlpha) =>
            NavStripPaint.WithAlpha(NavStripPaint.Mix(NeutralShadowInk, hue, ShadowHuePull), alpha);

        /// <summary>Where a raised face sits, px DOWN from rest: pressed, then lit, then hovered (lifts).</summary>
        public static double TravelFor(bool enabled, bool pressed, bool active, bool hovered)
        {
            if (!enabled) return 0;
            if (pressed) return PressTravelPx;
            if (active) return ActiveSinkPx;
            if (hovered) return -HoverLiftPx;
            return 0;
        }

        /// <summary>The drop band's length: none pressed or lit, longer on hover, RaisedPx at rest.</summary>
        public static double ShadowFor(bool enabled, bool pressed, bool active, bool hovered)
        {
            if (!enabled || pressed || active) return 0;
            return hovered ? RaisedPx + HoverLiftPx : RaisedPx;
        }
    }
}
