using System;

namespace ConditioningControlPanel.Services.AIService;

/// <summary>Shared, advisory-only hardware recommendation for the local AI wizard.</summary>
public static class LocalAiHardwareRules
{
        public const long Gb = 1L << 30;
        public const long RecommendedRamBytes = 16 * Gb;
        public const long RecommendedVramBytes = 8 * Gb;

        public enum Fit
        {
            /// <summary>Nothing could be read.</summary>
            Unknown,
            /// <summary>Under the recommendation: expect the whole PC to slow down while it thinks.</summary>
            Below,
            /// <summary>At or over the recommendation (it still costs the PC while it thinks).</summary>
            Ok
        }

        /// <summary>
        /// The verdict. Windows reports a little less than the installed memory (a 16 GB machine
        /// reads about 15.8 GB), so each figure gets 10% slack. A missing graphics figure does not
        /// count against the PC; a missing memory figure means nothing is known.
        /// </summary>
        public static Fit Judge(long ramBytes, long vramBytes)
        {
            if (ramBytes > 0 && ramBytes < RecommendedRamBytes * 0.9) return Fit.Below;
            if (vramBytes > 0 && vramBytes < RecommendedVramBytes * 0.9) return Fit.Below;
            return ramBytes > 0 ? Fit.Ok : Fit.Unknown;
        }

        /// <summary>Whole gigabytes for the card ("16 GB"); "?" when unknown.</summary>
        public static string FormatGb(long bytes)
            => bytes > 0 ? Math.Round(bytes / (double)Gb).ToString("0") + " GB" : "?";

}
