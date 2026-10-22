using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Banner
{
    /// <summary>
    /// The interlude act ids and the picker, moved out of the WPF head's MarqueeReadEffects (which
    /// keeps the acts themselves and forwards here) so every head draws acts by the same rule.
    /// </summary>
    public static class MarqueeReadActs
    {
        public const string GlitchHeal = "glitch-heal";
        public const string NumberFirst = "number-first";
        public const string Teletype = "teletype";
        public const string NeonWarmUp = "neon-warmup";
        public const string Redaction = "redaction";

        /// <summary>
        /// Pick an act. Never the one that just played, never one that says it cannot carry the
        /// line, and null only when nothing at all is eligible.
        ///
        /// <para>The roll is a plain index into whatever survived the filter, so a table that
        /// shrinks to one entry still plays that entry rather than falling over - the
        /// no-repeat rule yields to actually having something to show only when the table is down
        /// to a single usable act.</para>
        /// </summary>
        public static string? PickId(IReadOnlyList<string> ids, string? lastId,
                                     Func<string, bool> canPlay, Func<int, int> roll)
        {
            if (ids == null || ids.Count == 0) return null;

            var usable = new List<string>();
            foreach (var id in ids) if (canPlay(id)) usable.Add(id);
            if (usable.Count == 0) return null;

            var fresh = new List<string>();
            foreach (var id in usable) if (!string.Equals(id, lastId, StringComparison.Ordinal)) fresh.Add(id);

            var from = fresh.Count > 0 ? fresh : usable;
            return from[Math.Abs(roll(from.Count)) % from.Count];
        }
    }
}
