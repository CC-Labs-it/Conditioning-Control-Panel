using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

        /// <summary>Every act, in the WPF table's order (MarqueeReadEffects.Build).</summary>
        public static readonly IReadOnlyList<string> All = new[] { GlitchHeal, NumberFirst, Teletype, NeonWarmUp, Redaction };

        /// <summary>WPF NumberFirstEffect.Integer: the figure the number-first act rolls up.</summary>
        public static readonly Regex Integer = new(@"\d{1,7}", RegexOptions.Compiled);

        /// <summary>
        /// The acts' CanPlay (WPF MarqueeReadEffects.cs): every act needs a body; number-first also
        /// needs a rendered integer in it, and declines otherwise so the picker moves on.
        /// </summary>
        public static bool CanPlay(string id, string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;
            return id != NumberFirst || Integer.IsMatch(body);
        }

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
