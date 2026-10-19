namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// The two small buttons in the top-right corner of a mandatory video (owner, 2026-10-19):
    /// mute and close. Pure, so the rule is pinned by tests (MandatoryVideoControlsTests).
    /// </summary>
    internal static class MandatoryVideoControls
    {
        /// <summary>
        /// The X is the Esc door with a mouse: it shows only where Esc may dismiss the video.
        /// Hidden for the same "covers the screen on purpose" cases that keep a video full screen
        /// (<see cref="MandatoryVideoPresentation.ShouldFloat"/>): a strict video (Strict Lock,
        /// Lock Card, Possession, a leash or session strict override), a Lockdown, and a video
        /// inside a Chaos or Down the Rabbit Hole run.
        /// </summary>
        public static bool ShowClose(bool strict, bool lockdownActive, bool chaosRunning)
            => !strict && !lockdownActive && !chaosRunning;

        /// <summary>Mute ends nothing, so it shows on every mandatory video, strict ones too.</summary>
        public static bool ShowMute(bool strict, bool lockdownActive, bool chaosRunning) => true;

        /// <summary>Button diameter in DIPs (scaled by the monitor's DPI like every WPF size).</summary>
        public const double ButtonDip = 24;
        /// <summary>Gap from the video's top and right edges, and between the two buttons.</summary>
        public const double MarginDip = 10, GapDip = 6;
        /// <summary>Resting opacity; a hovered button is fully opaque.</summary>
        public const double RestOpacity = 0.6;
    }
}
