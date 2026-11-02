using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The V1/V2 avatar switch (Rain, handoff-1030/RAIN-V2-PLAN.md 2.1, lane R1). Pure: no App, no
    /// WPF. One GLOBAL setting (<c>AppSettings.AvatarGeneration</c>); a mod only plays V2 when the
    /// emote registry carries a <c>generation:"v2"</c> row for it (Bambi, Sissy, CCP Default; never
    /// Locked). Anything else falls back to today's (modId, avatarSet) answer with the setting left
    /// as it is, so a switch back to a Rain mod brings Rain back.
    /// </summary>
    public static class AvatarGenerationRule
    {
        public const string V1 = "v1";
        public const string V2 = "v2";

        /// <summary>"v2" (any case, padded) is V2; everything else, null included, is V1.</summary>
        public static string Normalize(string? generation)
            => string.Equals(generation?.Trim(), V2, StringComparison.OrdinalIgnoreCase) ? V2 : V1;

        public static bool IsV2(string? generation) => Normalize(generation) == V2;

        /// <summary>The pill's click: V1 -> V2, V2 -> V1.</summary>
        public static string Toggle(string? generation) => IsV2(generation) ? V1 : V2;

        /// <summary>
        /// The emote folder the tube plays. <paramref name="v2Folder"/> is the active mod's v2 row
        /// (null when it has none); it is passed through untouched, so an absolute folder (a baked
        /// look pack) works as well as a bare embedded one. <paramref name="v1Folder"/> is today's
        /// lookup and only runs when V2 does not apply.
        /// </summary>
        public static string? ResolveFolder(string? generation, string? v2Folder, Func<string?> v1Folder)
        {
            if (IsV2(generation) && !string.IsNullOrEmpty(v2Folder)) return v2Folder;
            return v1Folder();
        }

        /// <summary>The pill shows only for a mod with a v2 row, out of portrait mode and with no glitch up.</summary>
        public static bool PillVisible(bool modHasV2Row, bool portraitMode, bool glitchUp)
            => modHasV2Row && !portraitMode && !glitchUp;

        /// <summary>
        /// Parses <c>Resources/avatar_emotes_registry.json</c>. A row with <c>avatarSet</c> is an avatar
        /// set (arrows, level picker); a row with <c>generation</c> is a whole avatar and never a set.
        /// Rows missing a mod id or folder are skipped. Malformed JSON throws (the caller logs it).
        /// </summary>
        public static (List<(string modId, int set, string folder)> sets,
                       List<(string modId, string generation, string folder)> generations) ParseRegistry(string json)
        {
            var sets = new List<(string, int, string)>();
            var generations = new List<(string, string, string)>();
            var j = JObject.Parse(json);
            if (j["sets"] is JArray arr)
                foreach (var it in arr)
                {
                    var modId = (string?)it["modId"];
                    var set = (int?)it["avatarSet"];
                    var folder = (string?)it["folder"];
                    var generation = (string?)it["generation"];
                    if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(folder)) continue;
                    if (!string.IsNullOrEmpty(generation))
                        generations.Add((modId!, generation!, folder!));
                    else if (set.HasValue)
                        sets.Add((modId!, set.Value, folder!));
                }
            return (sets, generations);
        }
    }
}
