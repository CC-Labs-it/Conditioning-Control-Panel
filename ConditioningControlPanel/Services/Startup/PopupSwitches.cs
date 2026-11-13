namespace ConditioningControlPanel.Services.Startup
{
    /// <summary>
    /// Popups the owner switched off (2026-11-07, 7.2.3). The code behind each stays; set a switch
    /// back to true to bring that popup back. Static readonly rather than const, so the guarded code
    /// still compiles without unreachable-code warnings.
    /// </summary>
    internal static class PopupSwitches
    {
        /// <summary>"Media without the download": the online-media offer at an empty-assets dead end
        /// (<c>App.OfferRemoteMediaSource</c>). The online source stays one click away in Assets.</summary>
        public static readonly bool RemoteMediaOffer = false;

        /// <summary>"Post your achievements to the community Discord?" after a Discord link. The
        /// Share Achievements checkbox on the Discord page still does it; a row the user opens still asks.</summary>
        public static readonly bool DiscordSharePrompt = false;

        /// <summary>The once-ever tier celebration card and the small "Your Prime is on" card on a tier
        /// rise. The tier is still recorded, and the profile bubble keeps its fanfare.</summary>
        public static readonly bool TierCelebration = false;
    }
}
