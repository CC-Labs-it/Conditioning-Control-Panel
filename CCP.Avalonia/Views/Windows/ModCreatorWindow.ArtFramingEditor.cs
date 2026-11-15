using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        // Working copies keep pointer edits isolated until the dialog is accepted.
        internal static Dictionary<string, ModArtFraming> BeginFramingEdit(
            string slot, IReadOnlyDictionary<string, ModArtFraming>? stored)
        {
            var result = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
            foreach (var binding in ModArtFramingRegistry.FramableBindingsFor(slot))
                if (!result.ContainsKey(binding.SurfaceId))
                    result[binding.SurfaceId] = stored != null && stored.TryGetValue(binding.SurfaceId, out var value)
                        ? ModArtFramingRules.Sanitize(value) : new ModArtFraming();
            return result;
        }

        internal static void CommitFramingEdit(
            Dictionary<string, Dictionary<string, ModArtFraming>> destination, string slot,
            IReadOnlyDictionary<string, ModArtFraming> working)
        {
            var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
            foreach (var (surface, value) in working)
            {
                if (!ModArtFramingRegistry.IsFramable(slot, surface)) continue;
                var copy = ModArtFramingRules.Sanitize(value);
                if (!copy.IsDefault) kept[surface] = copy;
            }
            if (kept.Count == 0) destination.Remove(slot);
            else destination[slot] = kept;
        }

        internal static double ClampFramingCenter(double value, double size)
        {
            if (double.IsNaN(value) || double.IsNaN(size) || size <= 0 || size >= 1) return 0.5;
            return Math.Clamp(value, size / 2, 1 - size / 2);
        }

        internal static void PanFraming(ModArtFraming framing, double sourceAspect, double surfaceAspect,
            double startX, double startY, double deltaX, double deltaY)
        {
            var window = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surfaceAspect);
            framing.CenterX = ClampFramingCenter(startX - deltaX * window.Width, window.Width);
            framing.CenterY = ClampFramingCenter(startY - deltaY * window.Height, window.Height);
        }

        internal static void ZoomFraming(ModArtFraming framing, double sourceAspect, double surfaceAspect, bool zoomIn)
        {
            framing.Zoom = Math.Clamp(framing.Zoom * (zoomIn ? 1.12 : 1 / 1.12), 1, ModArtFramingRegistry.MaxZoom);
            var window = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surfaceAspect);
            framing.CenterX = ClampFramingCenter(framing.CenterX, window.Width);
            framing.CenterY = ClampFramingCenter(framing.CenterY, window.Height);
        }
    }
}
