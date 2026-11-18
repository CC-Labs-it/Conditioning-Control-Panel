using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ConditioningControlPanel;

/// <summary>
/// Where the full-screen picture wash (the glitch trigger bubble, the Chaos brain drain) draws its
/// picture. The wash window spans the whole desktop, and one picture filled across three monitors
/// side by side was scaled to their joint width and lost most of its height (tester, 7.3.1). Now the
/// picture is sized from each monitor: one tile per screen, each filling its own screen only.
/// One screen = one tile the size of the window, which is what it always drew. Pure maths.
/// </summary>
internal static class WashTiles
{
    /// <summary>
    /// The tiles in the window's own DIPs (origin = the stage's top-left). <paramref name="stagePx"/>
    /// and <paramref name="screensPx"/> are physical desktop pixels; <paramref name="dpiScale"/> is the
    /// window's scale (1 = 96 dpi). Screens outside the stage are dropped, a screen half outside is cut
    /// to the stage, mirrored screens count once. Never empty: no usable screen = the whole stage.
    /// </summary>
    internal static IReadOnlyList<Rect> For(Rect stagePx, IEnumerable<Rect> screensPx, double dpiScale)
    {
        double s = dpiScale > 0 && !double.IsInfinity(dpiScale) && !double.IsNaN(dpiScale) ? dpiScale : 1;
        var tiles = new List<Rect>();
        if (!stagePx.IsEmpty && stagePx.Width > 0 && stagePx.Height > 0)
        {
            foreach (var screen in screensPx ?? Enumerable.Empty<Rect>())
            {
                var cut = Rect.Intersect(stagePx, screen);
                if (cut.IsEmpty || cut.Width <= 0 || cut.Height <= 0) continue;
                var tile = new Rect((cut.X - stagePx.X) / s, (cut.Y - stagePx.Y) / s, cut.Width / s, cut.Height / s);
                if (!tiles.Contains(tile)) tiles.Add(tile);
            }
        }
        if (tiles.Count == 0)
            tiles.Add(new Rect(0, 0, Math.Max(0, stagePx.IsEmpty ? 0 : stagePx.Width) / s, Math.Max(0, stagePx.IsEmpty ? 0 : stagePx.Height) / s));
        return tiles.OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
    }
}
