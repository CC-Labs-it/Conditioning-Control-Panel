using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>Where on an anchor's bounds an event burst should originate.</summary>
    public enum FxBurstSpot
    {
        /// <summary>Dead centre. The default for cards, tiles and toasts.</summary>
        Center,

        /// <summary>The middle of the right edge - a progress bar's cap.</summary>
        RightEdge,

        /// <summary>The middle of the left edge.</summary>
        LeftEdge,

        /// <summary>Centre of the top edge - a banner that is about to be read.</summary>
        TopCenter,

        /// <summary>Centre of the bottom edge.</summary>
        BottomCenter,
    }

    /// <summary>Shared WPF event-burst bounds, clipping and origin rules. Coordinates are pixels in the host.</summary>
    public static class FxBurstAnchorRules
    {
        private const double EdgeSlackPx = 2.0;

        public static bool TryResolve((double X, double Y, double Width, double Height) anchorInLayer,
                                      (double Width, double Height) layerSize, FxBurstSpot spot, out (double X, double Y) origin)
        {
            origin = default;

            if (double.IsNaN(layerSize.Width) || double.IsNaN(layerSize.Height)) return false;
            if (layerSize.Width <= 1 || layerSize.Height <= 1) return false;

            if (double.IsNaN(anchorInLayer.X) || double.IsNaN(anchorInLayer.Y) ||
                double.IsNaN(anchorInLayer.Width) || double.IsNaN(anchorInLayer.Height)) return false;
            if (double.IsInfinity(anchorInLayer.X) || double.IsInfinity(anchorInLayer.Y) ||
                double.IsInfinity(anchorInLayer.Width) || double.IsInfinity(anchorInLayer.Height)) return false;
            // A zero-size anchor is a control that has never been measured (a collapsed tab, a
            // tile that has not been laid out): there is nothing on screen to burst from.
            if (anchorInLayer.Width <= 0 || anchorInLayer.Height <= 0) return false;

            // Off-screen anchors (scrolled out of a list, on a hidden tab that still has a
            // transform) get nothing. The slack keeps a bar cap sitting a hair past the edge.
            if ((anchorInLayer.X + anchorInLayer.Width) < EdgeSlackPx || (anchorInLayer.Y + anchorInLayer.Height) < EdgeSlackPx) return false;
            if (anchorInLayer.X > layerSize.Width - EdgeSlackPx) return false;
            if (anchorInLayer.Y > layerSize.Height - EdgeSlackPx) return false;

            double x = spot switch
            {
                FxBurstSpot.RightEdge => (anchorInLayer.X + anchorInLayer.Width),
                FxBurstSpot.LeftEdge => anchorInLayer.X,
                _ => anchorInLayer.X + anchorInLayer.Width / 2.0,
            };
            double y = spot switch
            {
                FxBurstSpot.TopCenter => anchorInLayer.Y,
                FxBurstSpot.BottomCenter => (anchorInLayer.Y + anchorInLayer.Height),
                _ => anchorInLayer.Y + anchorInLayer.Height / 2.0,
            };

            origin = (Clamp(x, layerSize.Width), Clamp(y, layerSize.Height));
            return true;

            static double Clamp(double v, double max) => Math.Max(0, Math.Min(max, v));
        }
    }
}
