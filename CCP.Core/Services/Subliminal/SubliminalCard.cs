using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The look and timing of one subliminal card, shared by every renderer: the WPF keep-alive
/// windows and solid-mode host cards (SubliminalService), the WPF compositor layer
/// (SubliminalLayer) and the Avalonia overlay (SubliminalOverlay). Each of them used to carry its
/// own copy of these numbers.
/// </summary>
public static class SubliminalCard
{
    /// <summary>Fade in and fade out, each side of the hold, unless the caller names its own.</summary>
    public const int DefaultFadeMs = 50;

    /// <summary>Bold text at this size in DIP (the renderer scales it per screen).</summary>
    public const double FontDip = 120;

    /// <summary>The 8 outline copies drawn in SubBorderColor under the main text, in DIP.
    /// Read-only: three renderers share it, so a write from one would change all of them.</summary>
    public static readonly IReadOnlyList<(double X, double Y)> OutlineOffsets = Array.AsReadOnly(new (double X, double Y)[]
    {
        (-3, -3), (3, -3), (-3, 3), (3, 3),
        (0, -4), (0, 4), (-4, 0), (4, 0),
    });

    /// <summary>
    /// Hold time: SubliminalDuration counts frames at ~17 ms each, minimum 100 ms. A caller may
    /// override it (the Deeper engine passes its timeline segment width), still at least 100 ms.
    /// </summary>
    public static int HoldMs(int frames, int? overrideMs = null) => Math.Max(100, overrideMs ?? frames * 17);

    /// <summary>A fade length: the caller's own, else <see cref="DefaultFadeMs"/>; never under 1 ms.</summary>
    public static int FadeMs(int? ms) => Math.Max(1, ms ?? DefaultFadeMs);

    /// <summary>A SubliminalOpacity percent as a 0..1 alpha.</summary>
    public static double Alpha(int percent) => Math.Clamp(percent / 100.0, 0, 1);
}
