namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// When a dashboard tile's art goes grey: the ONE answer both card controls ask.
    ///
    /// <para>The mosaic's off tiles used to rest at 62% opacity and nothing else, and the Discord
    /// verdict was that on and off look the same. Opacity on a dark wall reads as "a bit further
    /// away", not as "off". Colour does: an active tile is the only one wearing its own
    /// palette, and a glance down the wall says what START will run.</para>
    ///
    /// <para>Pure on purpose, so the priority order lives in one testable place: a lock veil
    /// wins over the mute (a locked tile keeps the look it always had and must not read as
    /// merely switched off), a tease keeps its blur, and a hover hands the colour back so an off
    /// tile still reads as live.</para>
    /// </summary>
    public static class CardMuteRule
    {
        /// <summary>How long the colour takes to drain or return on a state change or a hover.
        /// Interaction motion, so it runs whenever motion transitions does;
        /// under Off the tile snaps.</summary>
        public const int FadeMs = 200;

        /// <summary>A single tile: grey only when it opted into dimming, its feature is off, it is
        /// not locked, not teased, and the mouse is elsewhere.</summary>
        public static bool ShouldMute(bool dimWhenInactive, bool isActive, bool isLocked, bool hovered, bool teased)
            => dimWhenInactive && !isActive && !isLocked && !teased && !hovered;

        /// <summary>One half of a split tile: grey when its feature is off and the mouse has not
        /// committed the card to it. No opt-in here - a split tile only ever carries toggles.</summary>
        public static bool ShouldMuteHalf(bool isActive, bool halfHovered)
            => !isActive && !halfHovered;

        /// <summary>Snap or fade: fade only when motion is allowed AND the card is already on
        /// screen, so a tile never animates its first frame.</summary>
        public static int TransitionMs(bool allowTransitions, bool loaded)
            => allowTransitions && loaded ? FadeMs : 0;
    }

}
