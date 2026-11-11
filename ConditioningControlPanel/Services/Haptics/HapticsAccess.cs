namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Who may connect and drive toys. Owner call 2026-11-11: toy haptics are free for every
    /// account (they were Basic since the tier split). Every haptics door asks this one
    /// property - the mixer gate, the master switch, both Connect buttons, the setup wizard,
    /// the page veil and the entitlement lapse sweep - so a later tier move is one line here,
    /// not a hunt through six call sites that each re-asked HasPremiumAccess.
    /// </summary>
    public static class HapticsAccess
    {
        /// <summary>True for everyone, signed in or not.</summary>
        public static bool IsOpen => true;
    }
}
