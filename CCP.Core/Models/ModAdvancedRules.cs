using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>Shared WPF/Avalonia authoring limits for the Mod Creator Advanced panel.</summary>
    public static class ModAdvancedRules
    {
        public static ModTubeLayout? TubeLayout(string? x, string? detachedX, string? scale, string? y, string? detachedY)
        {
            int? Int(string? text, int min, int max) =>
                int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? Math.Clamp(n, min, max) : null;
            double? Double(string? text) =>
                double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
                    ? Math.Clamp(n, 0.1, 3.0) : null;
            var offX = Int(x, -1000, 1000);
            var detX = Int(detachedX, -1000, 1000);
            var avatarScale = Double(scale);
            var offY = Int(y, -500, 500);
            var detY = Int(detachedY, -500, 500);
            if (offX == null && detX == null && avatarScale == null && offY == null && detY == null) return null;
            return new ModTubeLayout { AvatarOffsetX = offX ?? 0, AvatarDetachedOffsetX = detX ?? 0,
                AvatarScale = avatarScale, AvatarOffsetY = offY ?? 0, AvatarDetachedOffsetY = detY ?? 0 };
        }

        public static string? Text(string? text, int maxLength)
        {
            text = text?.Trim();
            return string.IsNullOrEmpty(text) ? null : text.Length > maxLength ? text[..maxLength] : text;
        }

        public static Dictionary<string, string>? Tooltips(IEnumerable<(string? Key, string? Value)> rows)
        {
            Dictionary<string, string>? result = null;
            foreach (var (rawKey, rawValue) in rows)
            {
                var key = rawKey?.Trim();
                var value = Text(rawValue, 500);
                if (string.IsNullOrEmpty(key) || value == null) continue;
                result ??= new Dictionary<string, string>();
                if (result.Count < 30 || result.ContainsKey(key)) result[key] = value;
            }
            return result;
        }
    }

    public static class ModBubbleScaleRules
    {
        /// <summary>
        /// Decimals kept. An ambient bubble is 150-250 DIP, so a thousandth of a multiplier is a
        /// quarter of a pixel -- past that it is noise in the box and noise in mod.json.
        /// </summary>
        public const int StoredDecimals = 3;

        /// <summary>
        /// A manifest value reduced to something worth showing and worth writing back, or null for
        /// "the mod said nothing". Non-finite is DROPPED rather than clamped: NaN is a typo, not a
        /// request for the smallest legal bubble, and <c>BubbleSizing</c> already treats it as
        /// absent -- clamping it to 0.5 here would invent a decision the author never made.
        /// </summary>
        public static double? Sanitize(double? raw)
        {
            if (!raw.HasValue) return null;

            var value = raw.Value;
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;

            return Math.Round(
                Math.Clamp(value, Services.BubbleSizing.ModScaleMin, Services.BubbleSizing.ModScaleMax),
                StoredDecimals);
        }

        /// <summary>
        /// The box text for a manifest value: invariant culture (the same reason
        /// <c>ModArtFramingRegistry.TryParsePoint</c> is invariant -- a decimal comma round-trips
        /// into a different number on a different machine), and blank when there is nothing to show
        /// so a re-export cannot pin a value the author never chose.
        /// </summary>
        public static string? Format(double? raw) =>
            Sanitize(raw)?.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// What the box means on export. Blank or unparseable is null, which keeps the key out of
        /// mod.json entirely. An explicit 1.0 is KEPT: it changes nothing, but "this art is
        /// deliberately the app's own size" is a statement an author may want on the record.
        /// </summary>
        public static double? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return null;

            return Sanitize(value);
        }
    }
}
