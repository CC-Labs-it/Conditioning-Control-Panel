namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF <c>Services/Startup/PopupSwitches</c> (owner 2026-11-07, #4449): three popups are off. The code
    /// behind each stays; flip a shipped value to bring that popup back. The live fields exist only so the
    /// test assembly can drive the guarded doors (its module initializer turns them on; the switch tests
    /// run alone and put the shipped values back).
    /// </summary>
    internal static class PopupSwitches
    {
        /// <summary>"Media without the download": the online-media offer at an empty-assets dead end
        /// (<see cref="Platform.RemoteMediaOffer"/>). The online source stays one click away in Assets.</summary>
        internal const bool ShippedRemoteMediaOffer = false;

        /// <summary>"Post your achievements to the community Discord?" after a Discord link. The Share
        /// Achievements checkbox still does it; a row the user opens (userAsked) still asks.</summary>
        internal const bool ShippedDiscordSharePrompt = false;

        /// <summary>The once-ever tier celebration card and the small "Your Prime is on" card on a tier
        /// rise. The tier is still recorded, and the profile bubble keeps its fanfare.</summary>
        internal const bool ShippedTierCelebration = false;

        internal static bool RemoteMediaOffer = ShippedRemoteMediaOffer;
        internal static bool DiscordSharePrompt = ShippedDiscordSharePrompt;
        internal static bool TierCelebration = ShippedTierCelebration;

        /// <summary>Test seam: every popup door open (the module initializer) or back to the shipped values.</summary>
        internal static void SetAllForTests(bool on)
        {
            RemoteMediaOffer = on || ShippedRemoteMediaOffer;
            DiscordSharePrompt = on || ShippedDiscordSharePrompt;
            TierCelebration = on || ShippedTierCelebration;
        }
    }
}
