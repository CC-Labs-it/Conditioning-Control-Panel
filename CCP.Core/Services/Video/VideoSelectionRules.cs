using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The pure half of WPF VideoService's clip selection (<c>GetNextVideo</c> / <c>RefillVideoQueues</c>):
    /// where the next clip comes from and how the refill reports what it dropped. The pack-vs-local
    /// weighting is <see cref="PackMediaPool.ShouldDrawPack"/> and the length filter is
    /// <see cref="MandatoryVideoScheduler.KeepByLength"/>; both heads use those directly.
    /// </summary>
    internal static class VideoSelectionRules
    {
        /// <summary>True when a path is a remote clip rather than something on disk. Everything the
        /// pipeline does differently for remote media keys off this and nothing else.</summary>
        internal static bool IsRemoteMediaPath(string? path)
            => !string.IsNullOrEmpty(path)
               && (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Phase 4 remote blend: an empty local library always asks the remote buffer, "online" always
        /// does, and "mixed" rolls <paramref name="ratio"/> percent. The roll is only taken for "mixed",
        /// as WPF does. Only meaningful once remote media is enabled at all (consent + not "local").
        /// </summary>
        internal static bool WantsRemote(bool localEmpty, string? source, int ratio, Random rng)
            => localEmpty
               || string.Equals(source, "online", StringComparison.OrdinalIgnoreCase)
               || (string.Equals(source, "mixed", StringComparison.OrdinalIgnoreCase) && rng.Next(100) < ratio);

        /// <summary>Fisher-Yates, in place (WPF ShuffleList).</summary>
        internal static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// The #1124 refill funnel's "biggest drop": which step of the local walk lost the most files,
        /// so a Release log from "my folder has videos and the app says none" names the culprit. Ties go
        /// to the earlier step.
        /// </summary>
        internal static string RefillBiggestDrop(bool folderMissing, int seen, int keptExt, int keptSecurity,
            int keptEnabled, int keptDuration, int minSec, int maxSec)
        {
            if (folderMissing) return "the videos folder does not exist";
            if (seen == 0) return "the folder is empty";
            var drops = new (int Count, string Reason)[]
            {
                (seen - keptExt, $"extension filter ({seen - keptExt} file(s); supported: {string.Join(" ", MandatoryVideoScheduler.SupportedVideoExtensions)})"),
                (keptExt - keptSecurity, $"path/name security checks ({keptExt - keptSecurity} file(s))"),
                (keptSecurity - keptEnabled, $"the user's disabled-assets list ({keptSecurity - keptEnabled} file(s))"),
                (keptEnabled - keptDuration, $"duration filter ({keptEnabled - keptDuration} file(s); min {minSec}s max {maxSec}s)")
            };
            var worst = drops.OrderByDescending(d => d.Count).First();
            return worst.Count <= 0 ? "nothing was dropped" : worst.Reason;
        }
    }
}
