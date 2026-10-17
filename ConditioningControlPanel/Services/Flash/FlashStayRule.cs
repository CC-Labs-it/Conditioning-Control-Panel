namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// "Stay until popped" (owner, 2026-09-28, after Perly's feedback): an ambient flash never
    /// times out, it leaves when it is clicked or popped by a stare.
    ///
    /// <para>Three brakes, all owner calls: a 10-minute safety lifetime so a forgotten flash still
    /// goes, a 40-flash ceiling on screen (hydra spawns two per pop, so without it a stay run only
    /// ever grows), and the scheduler skipping its whole tick while the screen is full, so no
    /// whisper plays for a picture that could not be shown.</para>
    ///
    /// <para>Point-fired flashes (Deeper, chaos, the Back Room, bubbles delivering a picture) keep
    /// their authored lifetime: they were timed for a moment. It also needs clicking on: a flash
    /// nobody can click would just sit for the safety lifetime. Pure so it can be tested.</para>
    /// </summary>
    internal static class FlashStayRule
    {
        /// <summary>Safety lifetime of a stay flash: 10 minutes.</summary>
        internal const int SafetyLifetimeMs = 10 * 60 * 1000;

        /// <summary>Most stay flashes on screen at once (compositor and solid host).</summary>
        internal const int MaxOnScreen = 40;

        /// <summary>Does this spawn stay until popped?</summary>
        internal static bool Applies(bool stayEnabled, bool clickable, bool pointFired) =>
            stayEnabled && clickable && !pointFired;

        /// <summary>
        /// The concurrent-flash cap for this spawn. The classic per-flash window path keeps its own
        /// cap: forty layered windows is the native-memory climb that pinned it to ten.
        /// </summary>
        internal static int Cap(int baseCap, bool stay, bool sharedHost) =>
            stay && sharedHost ? MaxOnScreen : baseCap;

        /// <summary>
        /// The player's "Max on screen" slider (HydraLimit, 1..20) is the ceiling for every spawn
        /// path (Oct 14: bursts, bubble pops and hydra clicks stacked past it up to the 30/40 mode
        /// cap). The mode cap still wins when it is lower (classic windows: 10).
        /// A stay-until-popped spawn is the exception: its copy promises "Up to 40 at once", so
        /// its own ceiling (<see cref="Cap"/>: 40 on a shared host) stands and the 1..20 slider
        /// never trims it (ccp-bugs #1452, a regression of the Oct 14 change).
        /// </summary>
        internal static int OnScreen(int modeCap, int maxOnScreen, bool stay = false) =>
            stay ? modeCap : System.Math.Min(modeCap, System.Math.Clamp(maxOnScreen, 1, 20));

        /// <summary>True when the scheduler should skip this tick: the screen is already full.</summary>
        internal static bool ScreenFull(bool stay, int activeCount, int cap) => stay && activeCount >= cap;
    }
}
