using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

// Moved from WPF ModCreatorWindow.ArtFraming.cs; shared manifest filtering and clamping.
namespace ConditioningControlPanel
{
    internal static class ModArtFramingRules
    {
        internal const int StoredDecimals = 4;
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
                    if (ModArtFramingRegistry.FindSurface(surfaceId) == null) continue;
                    if (!ModArtFramingRegistry.IsFramable(slotKey, surfaceId)) continue;
                    var clean = Round(Sanitize(framing));
                    if (clean.IsDefault) continue;
                    kept[surfaceId] = clean;
                }
                if (kept.Count > 0) result[slotKey] = kept;
            }
            return result.Count > 0 ? result : null;
        }
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
                    if (!ModArtFramingRegistry.IsFramable(path, surfaceId)) continue;
                    var clean = Sanitize(framing);
                    if (clean.IsDefault) continue;
                    kept[surfaceId] = clean;
                }
                if (kept.Count > 0) result[path] = kept;
            }
            return result;
        }
        internal static ModArtFraming Sanitize(ModArtFraming framing) => new()
        {
            CenterX = Coordinate(framing.CenterX),
            CenterY = Coordinate(framing.CenterY),
            Zoom = ZoomOf(framing.Zoom),
        };
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
            return imageSlots.Any(kv => string.Equals(kv.Key, slotKey, StringComparison.OrdinalIgnoreCase)
                                        && !string.IsNullOrEmpty(kv.Value));
        }
        private static double Coordinate(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 0.5 : Math.Clamp(v, 0.0, 1.0);
        private static double ZoomOf(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 1.0 : Math.Clamp(v, 1.0, ModArtFramingRegistry.MaxZoom);
    }
}
