using System;
using System.Linq;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF's floating widgets (the avatar tube, its speech bubble, EMI's first-show stage and speech
/// window, the EMI desk windows, the transparent cards) are layered windows: Windows draws only
/// their painted pixels, no frame and no system shadow. Under WSLg every X window is drawn by
/// Weston's X window manager, which puts each undecorated window in a frame 32 px larger on each
/// side and paints a blurred shadow tile across it. Under a transparent window that tile is the
/// dim "ghost" rectangle the owner saw (11-06).
///
/// <para>This asks the WM for no shadow (<see cref="X11Overlay.SetNoFrameShadow"/>) on every
/// window that is undecorated AND transparent, the Avalonia form of WPF's
/// <c>WindowStyle=None + AllowsTransparency=True</c>, just before it maps. That covers every
/// such window, including ones added later, without a call in each constructor. Opaque windows (the
/// shell) and windows with system decorations keep the WM's frame and shadow.</para>
///
/// <para>Only under a Weston-family WM (WSLg, plain Weston), the one WM that draws this shadow.
/// On a compositing desktop (xfwm4, KWin, Mutter, openbox + picom) a transparent ARGB window
/// already shows only its painted pixels, so nothing is written there. <c>CCP_X11_FRAME_SHADOW</c>:
/// <c>keep</c> turns this off, <c>strip</c> writes the hint under any X11 WM (for probes).</para>
///
/// <para><b>Hooked on <see cref="Window.WindowOpenedEvent"/></b>, which Avalonia raises at the top of
/// <c>Window.ShowCore</c>, before <c>PlatformImpl.Show</c> maps the window (Avalonia 12.1.5,
/// Window.cs ShowCore). That timing matters: xwm paints the frame when the window maps, so a
/// later hint leaves the shadow already drawn.</para>
/// </summary>
internal static class FramelessShadow
{
    private static bool _installed;

    /// <summary>Headless observation: the windows the policy asked to strip, in show order.</summary>
    internal static Action<Window>? StripRequestedForTests;

    /// <summary>Test seam for the WM name (null = ask the X server).</summary>
    internal static Func<string?>? WindowManagerNameForTests;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => OnShowing(w));
    }

    /// <summary>WPF <c>WindowStyle=None + AllowsTransparency=True</c>: no system decorations and a
    /// transparent surface requested.</summary>
    internal static bool IsFramelessTransparent(Window w)
        => w.WindowDecorations == WindowDecorations.None
           && w.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent);

    /// <summary>Whether the X11 WM named <paramref name="wmName"/> draws the frame shadow this removes,
    /// after the <c>CCP_X11_FRAME_SHADOW</c> override (<c>keep</c> / <c>strip</c>).</summary>
    internal static bool WmDrawsGhostShadow(string? wmName, string? env)
    {
        if (string.Equals(env, "keep", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(env, "strip", StringComparison.OrdinalIgnoreCase)) return true;
        // Weston's xwm names its check window "Weston WM" (xwayland/window-manager.c, wm_create_wm_window).
        return wmName != null && wmName.StartsWith("Weston", StringComparison.OrdinalIgnoreCase);
    }

    private static void OnShowing(Window w)
    {
        if (!IsFramelessTransparent(w)) return;
        StripRequestedForTests?.Invoke(w);
        if (OperatingSystem.IsWindows() || X11Overlay.BackendOf(w.TryGetPlatformHandle()) != OverlayBackend.X11) return;
        var wm = WindowManagerNameForTests != null ? WindowManagerNameForTests() : X11Overlay.WindowManagerName;
        if (!WmDrawsGhostShadow(wm, Environment.GetEnvironmentVariable("CCP_X11_FRAME_SHADOW"))) return;
        if (!X11Overlay.SetNoFrameShadow(w))
            Log.Debug("FramelessShadow: could not set the no-shadow hint on {Window}", w.GetType().Name);
    }
}
