using System;
using System.Windows.Media;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.Depth
{
    /// <summary>
    /// The depth pass (nav polish wave 10, 2026-10-06): one lamp, three heights, "on is pressed in".
    /// Pure (no WPF element types) so tests pin the numbers without a window. Lanes only apply
    /// these through the brushes in Resources/Theme/Depth.xaml; nobody invents a shade by hand.
    ///
    /// The law:
    ///  * One lamp, top-left and slightly above the panel. Highlights sit on top edges, shadows fall
    ///    down and to the right. No surface argues with another.
    ///  * Three heights. Raised = you press it (chips, coins, planks, pills). Floating = you read
    ///    it (cards, quest rows, popovers). Sunken = it holds things (tracks, wells, trays, bezels).
    ///    A thing is exactly one of these; flat is not a height.
    ///  * On is pressed in (the Home mosaic rule from 2026-09-24, applied everywhere): a lit thing
    ///    sits down in its socket and glows, an off thing rises and goes grey. Hover lifts, press
    ///    drops, release springs back.
    ///  * Neon stays the signal. Depth says what a thing is; light says what it is doing.
    /// </summary>
    internal static class DepthRules
    {
        /// <summary>What a surface is. Every depth-bearing element picks exactly one.</summary>
        internal enum Height { Raised, Floating, Sunken }

        // ---- heights, in design-canvas pixels ---------------------------------------------------

        /// <summary>How far a raised thing stands off the sheet (the length of its drop shadow).</summary>
        internal const double RaisedPx = DepthLaw.RaisedPx;
        /// <summary>A hovered raised thing lifts this much more.</summary>
        internal const double HoverLiftPx = DepthLaw.HoverLiftPx;
        /// <summary>A pressed thing travels this far DOWN from rest (then springs back).</summary>
        internal const double PressTravelPx = DepthLaw.PressTravelPx;
        /// <summary>A lit ("on") thing sits this far down in its socket, at rest.</summary>
        internal const double ActiveSinkPx = DepthLaw.ActiveSinkPx;
        /// <summary>A floating card's shadow length.</summary>
        internal const double FloatPx = DepthLaw.FloatPx;
        /// <summary>Only START gets the chunky bevel: the biggest press target in the app.</summary>
        internal const double StartPx = DepthLaw.StartPx;
        /// <summary>A sunken well's inner shadow depth along its top edge.</summary>
        internal const double WellPx = DepthLaw.WellPx;
        /// <summary>The rail column throws this much shadow onto the page beside it.</summary>
        internal const double RailShadowPx = DepthLaw.RailShadowPx;
        /// <summary>A ledge (toolbar under a screen) throws this much shadow upward.</summary>
        internal const double LedgeUpPx = DepthLaw.LedgeUpPx;

        // ---- alphas (0..1) ----------------------------------------------------------------------

        /// <summary>The 1 px lit top edge of a raised face.</summary>
        internal const double HighlightAlpha = DepthLaw.HighlightAlpha;
        /// <summary>The 1 px shaded bottom edge of a raised face.</summary>
        internal const double ShadeAlpha = DepthLaw.ShadeAlpha;
        /// <summary>The soft sheen laid over a raised face from its top (fades out by 55%).</summary>
        internal const double SheenAlpha = DepthLaw.SheenAlpha;
        /// <summary>The drop shadow under a raised thing at rest, before the hue tint.</summary>
        internal const double ShadowAlpha = DepthLaw.ShadowAlpha;
        /// <summary>Inner shadow at a well's top edge (the strongest band in the system).</summary>
        internal const double WellTopAlpha = DepthLaw.WellTopAlpha;
        /// <summary>Inner shadow at a well's left edge (the lamp is top-left, so weaker).</summary>
        internal const double WellLeftAlpha = DepthLaw.WellLeftAlpha;
        /// <summary>The light foot line at a well's bottom edge: the rim catching the lamp.</summary>
        internal const double WellFootAlpha = DepthLaw.WellFootAlpha;
        /// <summary>A floating card's shadow (longer and softer than a raised thing's).</summary>
        internal const double FloatAlpha = DepthLaw.FloatAlpha;
        /// <summary>A ledge's upward shadow onto the screen above it.</summary>
        internal const double LedgeUpAlpha = DepthLaw.LedgeUpAlpha;
        /// <summary>The dark band inside a pressed face's top edge.</summary>
        internal const double PressedShadeAlpha = DepthLaw.PressedShadeAlpha;

        // ---- timings and motion -----------------------------------------------------------------

        /// <summary>Press down (fast), release spring (slower), hover lift.</summary>
        internal const int PressMs = DepthLaw.PressMs, ReleaseMs = DepthLaw.ReleaseMs, HoverMs = DepthLaw.HoverMs;
        /// <summary>The spring overshoot on release, in px, before settling at rest.</summary>
        internal const double ReleaseOvershootPx = DepthLaw.ReleaseOvershootPx;
        /// <summary>Hover tilt toward the pointer, the launcher tile's own number.</summary>
        internal const double TiltDegrees = DepthLaw.TiltDegrees;

        /// <summary>Motion-scaled milliseconds: Full = ms, Reduced = half, Off = 0 (static depth).</summary>
        internal static int Ms(int fullMs, MotionLevel level) => DepthLaw.Ms(fullMs, level);

        /// <summary>Tilt is the one motion of this pass and is allowed only where the launcher
        /// allows it: transitions on and a tier above Performance. Rail coins and Home tiles only.</summary>
        internal static bool TiltAllowed(MotionLevel level, PerformanceTier tier) =>
            level != MotionLevel.Off && tier != PerformanceTier.Performance;

        // ---- the shadow colour ------------------------------------------------------------------

        /// <summary>How far a shadow is pulled from black toward the section hue (owner default:
        /// tinted, so shadows blend with the page wash and the wave 9 edge).</summary>
        internal const double ShadowHuePull = DepthLaw.ShadowHuePull;

        /// <summary>The neutral shadow used where no section hue applies (dialogs, the launcher).</summary>
        internal static readonly Color NeutralShadowInk = Color.FromRgb((byte)((DepthLaw.NeutralShadowInk >> 16) & 0xFF), (byte)((DepthLaw.NeutralShadowInk >> 8) & 0xFF), (byte)(DepthLaw.NeutralShadowInk & 0xFF));

        /// <summary>A shadow colour for the section: deep ink pulled 28% toward the hue, at ShadowAlpha.</summary>
        internal static Color ShadowColor(Color hue) => ShadowColor(hue, ShadowAlpha);

        /// <summary>The same shadow at another strength (a card's FloatAlpha, a ledge's LedgeUpAlpha).</summary>
        internal static Color ShadowColor(Color hue, double alpha)
        {
            uint c = DepthLaw.ShadowColor(0xFF000000u | (uint)hue.R << 16 | (uint)hue.G << 8 | hue.B, alpha);
            return Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
        }

        // ---- travel -----------------------------------------------------------------------------

        /// <summary>Where a raised face sits, in px DOWN from rest, for a state. Pressed wins over
        /// everything; a lit thing sits in its socket; a hovered idle thing lifts; else rest.
        /// Disabled things never move (they are drawn flat-grey by their owner).</summary>
        internal static double TravelFor(bool enabled, bool pressed, bool active, bool hovered) =>
            DepthLaw.TravelFor(enabled, pressed, active, hovered);

        /// <summary>The drop shadow length for a state: none while pressed or lit (it sits on the
        /// sheet), longer on hover, RaisedPx at rest.</summary>
        internal static double ShadowFor(bool enabled, bool pressed, bool active, bool hovered) =>
            DepthLaw.ShadowFor(enabled, pressed, active, hovered);

    }
}
