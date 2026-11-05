using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>A rectangle in physical screen pixels (virtual-desktop space).</summary>
public readonly record struct PxRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>
/// ccp-bugs #1548: the launcher's one-time grow and the check that proves it painted.
///
/// <para>The grow used to set Height and then Top in DIPs: two separate moves, each converted
/// through the window's own DPI, on a three-monitor desktop with mixed sizes. It now works in
/// the one space Screen.WorkingArea and SetWindowPos share (physical pixels) and lands in a single
/// move that keeps the whole window inside ONE monitor's work area, so it never straddles a seam
/// or wakes a DPI change halfway through.</para>
///
/// <para>The paint probe reads a few screen pixels inside the launcher once it is in front. The
/// real launcher is a radial gradient behind a card and tiles, so its samples never agree; a
/// window whose render target stopped presenting shows one flat colour (the report's screenshot:
/// #0B0710 edge to edge). Pure on purpose, so both halves are rows in a test.</para>
/// </summary>
public static class LauncherPlacement
{
    /// <summary>Channel spread under which samples count as one flat colour.</summary>
    public const int BlankTolerance = 6;

    /// <summary>
    /// Where the window goes after growing to <paramref name="newHeightPx"/>: same width, grown
    /// about its centre, then pushed fully inside <paramref name="workArea"/>. Height and width
    /// are capped at the work area, so the result always sits on that one monitor.
    /// </summary>
    public static PxRect GrowWithin(PxRect window, PxRect workArea, int newHeightPx)
    {
        int w = Math.Min(window.Width, Math.Max(1, workArea.Width));
        int h = Math.Max(window.Height, newHeightPx);
        if (workArea.Height > 0) h = Math.Min(h, workArea.Height);
        h = Math.Max(1, h);

        int top = window.Top - (h - window.Height) / 2;
        int left = window.Left;
        top = Clamp(top, workArea.Top, workArea.Bottom - h);
        left = Clamp(left, workArea.Left, workArea.Right - w);
        return new PxRect(left, top, w, h);
    }

    /// <summary>DIPs to pixels for this window's DPI scale, rounded the way WPF sizes a window.</summary>
    public static int ToPx(double dip, double scale)
    {
        if (double.IsNaN(dip) || dip <= 0) return 0;
        if (double.IsNaN(scale) || scale <= 0) scale = 1;
        return (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Sample points for the paint probe: a 3x3 grid inset 15 percent from each edge of the
    /// window, so the pink edge and the title bar never decide the answer.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> ProbePoints(PxRect window)
    {
        var pts = new List<(int, int)>(9);
        if (window.Width < 20 || window.Height < 20) return pts;
        double[] f = { 0.15, 0.5, 0.85 };
        foreach (var fy in f)
            foreach (var fx in f)
                pts.Add((window.Left + (int)(window.Width * fx), window.Top + (int)(window.Height * fy)));
        return pts;
    }

    /// <summary>
    /// True when every sampled colour (0xRRGGBB) sits within <see cref="BlankTolerance"/> of the
    /// others on each channel: the window is one flat colour, so nothing of the launcher painted.
    /// Fewer than four samples is never called blank.
    /// </summary>
    public static bool LooksBlank(IReadOnlyList<int> rgb)
    {
        if (rgb == null || rgb.Count < 4) return false;
        int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
        foreach (var c in rgb)
        {
            int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
            gMin = Math.Min(gMin, g); gMax = Math.Max(gMax, g);
            bMin = Math.Min(bMin, b); bMax = Math.Max(bMax, b);
        }
        return rMax - rMin <= BlankTolerance && gMax - gMin <= BlankTolerance && bMax - bMin <= BlankTolerance;
    }

    private static int Clamp(int v, int lo, int hi) => hi < lo ? lo : Math.Min(Math.Max(v, lo), hi);
}
