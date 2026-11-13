using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The player's own "Voice Line" clips (Phrase manager, category VoiceLine, each with a recorded
    /// audio file) on the idle timer. Until 54b4c5c653 (2026-06-11) every idle tick spoke a random
    /// voice line; that commit handed idle chatter to the bark pool and left the voice-line path with
    /// no callers, so a player's own lines only played inside flash audio (Discord report, Nov 2026).
    ///
    /// <para>Only the player's OWN clips come back here. The built-in flash voice lines stay flash
    /// material (they repeated badly as chatter), and they are Bambi-only by
    /// <see cref="ModAudioPolicy"/> anyway. A phrase with no clip on disk is never picked, so nothing
    /// here ever needs a synthetic voice.</para>
    ///
    /// <para>The idle slider still sets the cadence; one tick in <see cref="OneIn"/> goes to an own
    /// line, the rest to the Idle bark pool as before. The line is unprompted, so
    /// WPF <c>CompanionIdleVoice</c> (Avalonia <c>MayVoice</c>) holds its sound while the engine is off.</para>
    /// </summary>
    public static class IdleVoiceLines
    {
        /// <summary>One idle tick in this many speaks an own voice line (when the player has any).</summary>
        public const int OneIn = 3;

        /// <summary>True when this tick goes to an own voice line. <paramref name="roll"/> is
        /// <c>Random.Next(OneIn)</c>.</summary>
        public static bool TakesTurn(int ownLineCount, int roll) => ownLineCount > 0 && roll == 0;

        /// <summary>
        /// The player's enabled VoiceLine phrases whose clip exists under <paramref name="folder"/>,
        /// as full paths. Phrases in other categories, disabled ones and ones without audio are left out.
        /// </summary>
        public static List<string> OwnClips(IEnumerable<CustomCompanionPhrase>? phrases, string folder, Func<string, bool> exists)
        {
            var result = new List<string>();
            if (phrases == null) return result;
            foreach (var p in phrases)
            {
                if (p == null || !p.Enabled || string.IsNullOrEmpty(p.AudioFileName)) continue;
                if (!string.Equals(p.Category, CompanionPhraseIds.VoiceLineCategory, StringComparison.Ordinal)) continue;
                var full = Path.Combine(folder, p.AudioFileName);
                if (exists(full)) result.Add(full);
            }
            return result;
        }

        /// <summary>A random clip, never the one played last time when there is another to pick.</summary>
        public static string? Pick(IReadOnlyList<string> clips, string? last, Random rng)
        {
            if (clips.Count == 0) return null;
            if (clips.Count == 1) return clips[0];
            var pick = clips[rng.Next(clips.Count)];
            if (last != null && string.Equals(pick, last, StringComparison.OrdinalIgnoreCase))
            {
                // Step to a neighbour instead of rerolling, so the pick stays one draw.
                var i = (IndexOf(clips, pick) + 1 + rng.Next(clips.Count - 1)) % clips.Count;
                pick = clips[i];
            }
            return pick;
        }

        private static int IndexOf(IReadOnlyList<string> clips, string value)
        {
            for (int i = 0; i < clips.Count; i++)
                if (string.Equals(clips[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }
    }
}
