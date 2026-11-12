using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        private readonly Dictionary<string, Dictionary<string, ModArtFraming>> _artFraming = new(StringComparer.OrdinalIgnoreCase);

        private void DropArtFraming(string key) => _artFraming.Remove(key);

        private void PopulateArtFramingFromManifest(ModManifest manifest)
        {
            _artFraming.Clear();
            foreach (var (path, surfaces) in ModArtFramingRules.ReadManifestSection(manifest.ArtFraming))
            {
                var key = _imageSlots.Keys.FirstOrDefault(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase));
                if (key != null) _artFraming[key] = surfaces;
            }
        }
    }

    internal static class ModArtFramingRules
    {
        /// <summary>
        /// Decimals kept in mod.json. A drag produces numbers like 0.47321981, and four decimals
        /// is far finer than a pixel on any of these surfaces -- the rest is noise an author has to
        /// read past when they open the file.
        /// </summary>
        internal const int StoredDecimals = 4;

        /// <summary>
        /// The manifest's <c>artFraming</c> section, or null when there is nothing worth writing
        /// (which keeps the key out of the JSON entirely, since export ignores nulls).
        /// </summary>
        /// <param name="framings">The editor's live map: slot key → surface id → framing.</param>
        /// <param name="imageSlots">The editor's slot map, slot key → picked file or null.</param>
        internal static Dictionary<string, Dictionary<string, ModArtFraming>>? BuildManifestSection(
            IReadOnlyDictionary<string, Dictionary<string, ModArtFraming>> framings,
            IReadOnlyDictionary<string, string?> imageSlots)
        {
            var result = new Dictionary<string, Dictionary<string, ModArtFraming>>(StringComparer.Ordinal);
            if (framings == null) return null;

            foreach (var (slotKey, perSurface) in framings)
            {
                if (string.IsNullOrWhiteSpace(slotKey) || perSurface == null) continue;
                if (!HasImage(imageSlots, slotKey)) continue;

                var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
                foreach (var (surfaceId, framing) in perSurface)
                {
                    if (framing == null) continue;

                    // An id this build does not know cannot have been framed against a real
                    // preview, so it is not written back. The cost is that a mod authored on a
                    // LATER build and re-exported here loses that surface's framing -- accepted,
                    // because the alternative is shipping numbers nobody could see or check.
                    if (ModArtFramingRegistry.FindSurface(surfaceId) == null) continue;

                    // Nor a pair the author was never offered. The editor cannot produce one, so
                    // this only catches what came in from a hand-edited mod.json -- and the runtime
                    // ignores framing on a non-framable pair (ResolveViewbox hands it the whole
                    // image), so writing it back would leave numbers in the file that describe
                    // nothing and invite the next reader to "fix" a crop that never happens.
                    if (!ModArtFramingRegistry.IsFramable(slotKey, surfaceId)) continue;

                    var clean = Round(Sanitize(framing));
                    if (clean.IsDefault) continue;
                    kept[surfaceId] = clean;
                }

                if (kept.Count > 0) result[slotKey] = kept;
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// The same shape coming the other way, for the load/import path: sanitized, no-ops and
        /// unknown surface ids dropped, never throwing on a hand-edited or newer-than-us manifest.
        /// Empty (not null) when there is nothing usable, so callers need no null dance.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, ModArtFraming>> ReadManifestSection(
            IReadOnlyDictionary<string, Dictionary<string, ModArtFraming>>? artFraming)
        {
            var result = new Dictionary<string, Dictionary<string, ModArtFraming>>(StringComparer.OrdinalIgnoreCase);
            if (artFraming == null) return result;

            foreach (var (path, perSurface) in artFraming)
            {
                if (string.IsNullOrWhiteSpace(path) || perSurface == null) continue;

                var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
                foreach (var (surfaceId, framing) in perSurface)
                {
                    if (framing == null) continue;
                    if (ModArtFramingRegistry.FindSurface(surfaceId) == null) continue;

                    // Dropped at the door rather than on the way out, so the editor never holds a
                    // framing it has no preview for: there is no Frame button on a non-framable
                    // pair, so a hand-edited entry would otherwise sit in memory invisible and
                    // unfixable, and still be counted by the "Framed for N surfaces" label.
                    if (!ModArtFramingRegistry.IsFramable(path, surfaceId)) continue;

                    var clean = Sanitize(framing);
                    if (clean.IsDefault) continue;
                    kept[surfaceId] = clean;
                }

                if (kept.Count > 0) result[path] = kept;
            }

            return result;
        }

        /// <summary>
        /// A copy with every field inside its legal range. <c>ToViewbox</c> clamps too, but the
        /// editor's sliders, readouts and <c>IsDefault</c> checks all read these values directly,
        /// and a NaN out of a hand-edited mod.json would poison all three.
        /// </summary>
        internal static ModArtFraming Sanitize(ModArtFraming framing) => new()
        {
            CenterX = Coordinate(framing.CenterX),
            CenterY = Coordinate(framing.CenterY),
            Zoom = ZoomOf(framing.Zoom),
        };

        /// <summary>A copy rounded to what mod.json stores.</summary>
        internal static ModArtFraming Round(ModArtFraming framing) => new()
        {
            CenterX = Math.Round(framing.CenterX, StoredDecimals),
            CenterY = Math.Round(framing.CenterY, StoredDecimals),
            Zoom = Math.Round(framing.Zoom, StoredDecimals),
        };

        private static bool HasImage(IReadOnlyDictionary<string, string?> imageSlots, string slotKey)
        {
            if (imageSlots == null) return false;
            if (imageSlots.TryGetValue(slotKey, out var picked)) return !string.IsNullOrEmpty(picked);

            // The editor's slot map is ordinal, so fall back to a case-insensitive sweep for a
            // framing whose key came out of a hand-edited manifest with different casing.
            return imageSlots.Any(kv => string.Equals(kv.Key, slotKey, StringComparison.OrdinalIgnoreCase)
                                        && !string.IsNullOrEmpty(kv.Value));
        }

        private static double Coordinate(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 0.5 : Math.Clamp(v, 0.0, 1.0);

        private static double ZoomOf(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 1.0 : Math.Clamp(v, 1.0, ModArtFramingRegistry.MaxZoom);
    }
}
