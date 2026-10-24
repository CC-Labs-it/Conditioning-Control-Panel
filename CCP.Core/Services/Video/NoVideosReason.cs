using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// ccp-bugs #1352: what the "no videos" dialog should blame. The refill funnel already counts
    /// how many enabled files the duration filter threw out; when that filter is what emptied the
    /// pool, telling the user to "add files" sends them to the wrong place.
    /// </summary>
    public static class NoVideosReason
    {
        /// <summary>
        /// True when there were enabled local videos and the length filter kept none of them.
        /// Negative counts mean the funnel has not run yet this launch, so the old text stands.
        /// </summary>
        public static bool LengthFilterEmptied(int keptEnabled, int keptDuration)
            => keptEnabled > 0 && keptDuration == 0;

        /// <summary>The filter range the way the panel's sliders read: 52s, 2m 50s. An unset side is 0s or ∞.</summary>
        public static string FormatRange(int minSeconds, int maxSeconds)
            => $"{Format(minSeconds, "0s")} - {Format(maxSeconds, "∞")}";

        /// <summary>WPF TriggerVideo: describe the length filter first, then installed/active packs,
        /// then the add-files guidance. Both heads use the same localized message.</summary>
        public static string Message(string videosPath, int keptEnabled, int keptDuration,
            int minSeconds, int maxSeconds, int installedPacks, int activePacks)
        {
            if (LengthFilterEmptied(keptEnabled, keptDuration))
                return Loc.GetF("video_length_filter_emptied", keptEnabled, FormatRange(minSeconds, maxSeconds));
            var message = Loc.GetF("video_no_videos_found", videosPath) + "\n\n";
            if (installedPacks > 0 && activePacks == 0)
                message += Loc.GetF("video_packs_installed_none_active", installedPacks) + "\n"
                    + Loc.Get("video_enable_packs_hint") + "\n\n";
            else if (activePacks > 0)
                message += Loc.GetF("video_active_packs_no_videos", activePacks) + "\n\n";
            return message + Loc.Get("video_add_files_hint");
        }

        private static string Format(int seconds, string unset)
        {
            if (seconds <= 0) return unset;
            if (seconds < 60) return $"{seconds}s";
            var rem = seconds % 60;
            return rem == 0 ? $"{seconds / 60}m" : $"{seconds / 60}m {rem}s";
        }
    }
}
