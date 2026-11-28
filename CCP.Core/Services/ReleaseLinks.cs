namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where a user is sent to get a build. The updater that downloads and runs an installer is
    /// per-platform and stays in a head; the address is not, and every head's "download it
    /// yourself" fallback must point at the same page.
    /// </summary>
    public static class ReleaseLinks
    {
        /// <summary>The GitHub releases page — the manual-download fallback on every head.</summary>
        public const string ReleasesPageUrl =
            "https://github.com/CC-Labs-it/Conditioning-Control-Panel/releases/latest";

        /// <summary>Every release, newest first. A release's own page is <c>{ReleasesListUrl}/tag/{tag}</c>;
        /// the Linux head opens that page, never /releases/latest, which is the Windows-only build (ccp-bugs #1627).</summary>
        public const string ReleasesListUrl =
            "https://github.com/CC-Labs-it/Conditioning-Control-Panel/releases";
    }
}
