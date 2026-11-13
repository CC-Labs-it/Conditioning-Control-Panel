namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// How loudly a progression perk announces itself (owner, 2026-11-05). The payout never
    /// depends on this: Pink Rush 3x, lucky 10x / 20x and the Sparkle Boost XP pay in every mode.
    ///
    ///   Full    - everything: the cards and pings, plus the in-place visuals.
    ///   NoCards - the old SuppressPerkNotifications = true (meadow, 2026-08-18): no lucky toast,
    ///             Pink Rush card or quest popup and their sounds; the visuals stay.
    ///   Off     - no cards AND no Pink Rush screen wash, no lucky gold glow, no lucky or
    ///             Sparkle Boost sparkle burst and pink flash glow. The small Enhancements-tab
    ///             Pink Rush indicator stays, so a running window still has one signal.
    /// </summary>
    public enum PerkEffectsMode
    {
        Full = 0,
        NoCards = 1,
        Off = 2,
    }

    /// <summary>Pure rules for <see cref="PerkEffectsMode"/>, pinned by PerkEffectsModeTests.</summary>
    public static class PerkEffectsRules
    {
        /// <summary>Anything unknown (a hand-edited file, a future value) reads as Full.</summary>
        public static PerkEffectsMode Clamp(PerkEffectsMode mode)
            => mode is PerkEffectsMode.Full or PerkEffectsMode.NoCards or PerkEffectsMode.Off
                ? mode : PerkEffectsMode.Full;

        /// <summary>ComboBox index to mode (and back through the enum's int value).</summary>
        public static PerkEffectsMode FromIndex(int index) => Clamp((PerkEffectsMode)index);

        /// <summary>Cards, toasts and their pings show only in Full.</summary>
        public static bool ShowsCards(PerkEffectsMode mode) => Clamp(mode) == PerkEffectsMode.Full;

        /// <summary>The wash, the gold glow and the sparkles show in every mode but Off.</summary>
        public static bool ShowsVisuals(PerkEffectsMode mode) => Clamp(mode) != PerkEffectsMode.Off;

        /// <summary>
        /// Migration from the old bool. True lifts Full to NoCards and never lowers a stronger
        /// choice; false changes nothing, so a file holding both fields loads the same in either
        /// property order.
        /// </summary>
        public static PerkEffectsMode FromLegacySuppress(PerkEffectsMode current, bool suppress)
            => suppress && Clamp(current) == PerkEffectsMode.Full ? PerkEffectsMode.NoCards : Clamp(current);
    }
}
