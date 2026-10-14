using System.Windows;

namespace ConditioningControlPanel.Services
{
    /// <summary>The WPF geometry adapter for the shared Core event-burst rules.</summary>
    public static class FxBurstAnchor
    {
        public static bool TryResolve(Rect anchorInLayer, Size layerSize, FxBurstSpot spot, out Point origin)
        {
            var ok = FxBurstAnchorRules.TryResolve(
                (anchorInLayer.X, anchorInLayer.Y, anchorInLayer.Width, anchorInLayer.Height),
                (layerSize.Width, layerSize.Height), spot, out var point);
            origin = new Point(point.X, point.Y);
            return ok;
        }
    }
}
