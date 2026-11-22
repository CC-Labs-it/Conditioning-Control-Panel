using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>One ring pulse round the profile avatar: a gold ring rises (IN), holds, then grows out and fades (OUT).</summary>
    public readonly record struct AvatarPulsePlan(double RiseMs, double HoldMs, double FallMs, double GrowTo, double Peak);

    /// <summary>
    /// "See it on your profile" lands on the profile page with one short highlight on the avatar, so the eye finds the
    /// new frame. Motion Off: no pulse at all. Reduced: a smaller, slower one. Gentle visuals: the rise never under
    /// 300 ms, a long soft fade, no second beat (Services/Safety/GentleVisuals rules). Pure.
    /// </summary>
    public static class OwnAvatarPulse
    {
        /// <summary>Waits this long after the tab is shown, so the pulse plays on a page that has settled.</summary>
        public const int DelayMs = 380;

        public static AvatarPulsePlan? For(MotionLevel motion, bool gentle)
        {
            if (motion == MotionLevel.Off) return null;
            if (gentle) return new AvatarPulsePlan(450, 300, 1100, 1.10, 0.75);
            if (motion == MotionLevel.Reduced) return new AvatarPulsePlan(320, 220, 700, 1.12, 0.85);
            return new AvatarPulsePlan(220, 180, 620, 1.28, 1.0);
        }
    }
}
