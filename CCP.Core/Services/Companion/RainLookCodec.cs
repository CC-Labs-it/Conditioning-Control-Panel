using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// Rain's wardrobe look as <c>AppSettings.RainLook</c> stores it (Rain avatar brief, contract 4; the
    /// same codec as the 7.2 Avalonia head on feat/rain-avatar):
    /// <c>{"rev":1,"items":{"&lt;slot&gt;":"&lt;itemId&gt;","accessories":["&lt;itemId&gt;",...]},"colors":{"&lt;channel&gt;":"#rrggbb"}}</c>.
    /// Empty = Rain's authored default look. The fitting room page (Resources/web/rain-wardrobe) posts it;
    /// the host keeps only what <see cref="Normalize"/> returns. Accessories are the one multi-pick slot.
    /// PURE: no settings, no IO.
    /// </summary>
    public static class RainLookCodec
    {
        public const int Rev = 1;

        /// <summary>A look bigger than this is not a look (89 choices fit in well under 2 KB).</summary>
        public const int MaxChars = 8192;

        /// <summary>The page's nine colour channels (wardrobe-colors.js).</summary>
        public static readonly IReadOnlyList<string> Channels = new[]
            { "fabric", "trim", "hair", "skin", "eyes", "lips", "nails", "shoes", "accessories" };

        public const string AccessoriesSlot = "accessories";

        private const int MaxAccessories = 16;

        private static readonly Regex SlotPattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant);
        private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex HexPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The stored form of <paramref name="json"/>: canonical (slots and channels sorted, colours lower-case),
        /// unknown or malformed parts dropped, and any item <paramref name="isLocked"/> refuses (slot, id) left
        /// out. Returns "" for null, blank, unreadable, another rev, or a look with nothing in it (= the default).
        /// </summary>
        public static string Normalize(string? json, Func<string, string, bool>? isLocked = null)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxChars) return "";
            JObject o;
            try { o = JObject.Parse(json); }
            catch (JsonException) { return ""; }
            if (o["rev"] is not JValue { Type: JTokenType.Integer } rev || rev.Value<long>() != Rev) return "";
            bool Locked(string slot, string id) => isLocked?.Invoke(slot, id) == true;

            var items = new SortedDictionary<string, object>(StringComparer.Ordinal);
            if (o["items"] is JObject rawItems)
            {
                foreach (var p in rawItems.Properties())
                {
                    if (!SlotPattern.IsMatch(p.Name)) continue;
                    if (p.Name == AccessoriesSlot)
                    {
                        if (p.Value is not JArray list) continue;
                        var ids = list.Where(t => t.Type == JTokenType.String).Select(t => (string)t!)
                            .Where(IsId).Where(id => !Locked(AccessoriesSlot, id))
                            .Distinct(StringComparer.Ordinal).Take(MaxAccessories).ToList();
                        items[p.Name] = ids;
                    }
                    else if (p.Value.Type == JTokenType.String && IsId((string?)p.Value) && !Locked(p.Name, (string)p.Value!))
                        items[p.Name] = (string)p.Value!;
                }
            }

            var colors = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (o["colors"] is JObject rawColors)
            {
                foreach (var p in rawColors.Properties())
                {
                    if (!Channels.Contains(p.Name) || p.Value.Type != JTokenType.String) continue;
                    var hex = (string)p.Value!;
                    if (HexPattern.IsMatch(hex)) colors[p.Name] = hex.ToLowerInvariant();
                }
            }

            if (items.Count == 0 && colors.Count == 0) return "";
            return JsonConvert.SerializeObject(new { rev = Rev, items, colors }, Formatting.None);
        }

        /// <summary>True when <paramref name="stored"/> means Rain's authored default look.</summary>
        public static bool IsDefault(string? stored) => Normalize(stored).Length == 0;

        private static bool IsId(string? id) => id != null && IdPattern.IsMatch(id);
    }
}
