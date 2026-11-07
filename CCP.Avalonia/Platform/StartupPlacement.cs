using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Where an ownerless startup window goes. WPF WindowStartupLocation.CenterScreen centres on the
/// monitor under the cursor (Window.CalculateWindowLocation: GetCursorPos + MonitorFromPoint), with the
/// primary as the fallback. Avalonia 12's CenterScreen found no screen at all under WSLg's XWayland
/// (logged: Screens.Primary null, no RandR primary, ScreenFromWindow null at 0,0, which lies on no
/// monitor: rdp-0 1680,0 2560x1440 and rdp-2 0,235 1680x1050) and left the splash at 0,0, which the
/// owner saw off to the lower left. This picks the screen itself.
/// </summary>
internal static class StartupPlacement
{
    /// <summary>The screen to centre on: the one containing the pointer, else the primary, else the first
    /// listed. "Containing", not WPF's nearest: XWayland reports the last pointer position over an X window,
    /// so a pointer on no monitor says nothing about where the user is. -1 when there are no screens.</summary>
    internal static int Pick(IReadOnlyList<PixelRect> bounds, int primary, PixelPoint? pointer)
    {
        if (bounds.Count == 0) return -1;
        if (pointer is { } p)
            for (var i = 0; i < bounds.Count; i++)
                if (bounds[i].Contains(p)) return i;
        return primary >= 0 && primary < bounds.Count ? primary : 0;
    }

    /// <summary>The screen the main window opens on: WPF MainWindow.CenterOnPrimaryScreen's primary; with no
    /// primary reported (WSLg's XWayland has none) the splash's choice, the screen under the pointer.</summary>
    internal static int PickForShell(IReadOnlyList<PixelRect> bounds, int primary, PixelPoint? pointer) =>
        primary >= 0 && primary < bounds.Count ? primary : Pick(bounds, -1, pointer);

    /// <summary>The Linux main window's first size against WPF's 1661 x 1002 DIP. Owner 11-06 took it 20% smaller;
    /// owner 11-07 took it 25% bigger again (it looked small beside the tube), which is WPF's size. A 1680 x 1050
    /// monitor is still handled by the work-area fit. Never under the XAML floors.</summary>
    internal const double LinuxShellScale = 0.8 * 1.25;

    internal static (double Width, double Height) ShellSize(double width, double height, double minWidth, double minHeight, bool linux) =>
        linux ? (Math.Max(minWidth, Math.Round(width * LinuxShellScale)), Math.Max(minHeight, Math.Round(height * LinuxShellScale)))
              : (width, height);

    /// <summary>Top-left, in physical pixels, that centres a <paramref name="widthDip"/> x
    /// <paramref name="heightDip"/> window in <paramref name="workArea"/> at <paramref name="scaling"/>.</summary>
    internal static PixelPoint CenterIn(PixelRect workArea, double widthDip, double heightDip, double scaling)
    {
        var k = scaling > 0 ? scaling : 1.0;
        var w = (int)Math.Round(widthDip * k);
        var h = (int)Math.Round(heightDip * k);
        return new PixelPoint(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2);
    }

    /// <summary>WPF CenterScreen for an ownerless window that is about to be shown: switches it to Manual
    /// and places it. Leaves Avalonia's own CenterScreen in charge when there is no screen to ask.</summary>
    internal static void CenterOnPointerScreen(Window window)
    {
        try
        {
            var screens = window.Screens.All;
            var at = Pick(screens.Select(s => s.Bounds).ToArray(), IndexOfPrimary(window), Pointer());
            if (at < 0 || double.IsNaN(window.Width) || double.IsNaN(window.Height)) return;
            var screen = screens[at];
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = CenterIn(screen.WorkingArea, window.Width, window.Height, screen.Scaling);
            ReassertAfterMap(window, window.Position);
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[Placement] startup centring failed; Avalonia places the window"); }
    }

    /// <summary>WSLg's window manager (Weston's xwm) ignores where a new X window asks to be on its first map but
    /// honours a move after it (owner's PC 2026-11-07: the splash landed low-left, the shell asked for 2129,219 and
    /// was put at 1952,37; an XConfigureWindow after the map went exactly where asked). So on Linux the startup
    /// position is sent once more after the window is shown. Window managers that honoured it see no change.</summary>
    internal static void ReassertAfterMap(Window window, PixelPoint at, bool? linux = null)
    {
        if (!(linux ?? OperatingSystem.IsLinux())) return;
        EventHandler? opened = null;
        opened = (_, _) =>
        {
            window.Opened -= opened;
            void Move() { if (window.IsVisible && window.WindowState == WindowState.Normal) window.Position = at; }
            Dispatcher.UIThread.Post(Move, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(Move, TimeSpan.FromMilliseconds(250));   // a WM that maps late
        };
        window.Opened += opened;
    }

    private static int IndexOfPrimary(Window window)
    {
        var all = window.Screens.All;
        for (var i = 0; i < all.Count; i++) if (all[i].IsPrimary) return i;
        return -1;
    }

    // Physical desktop pixels, the space Screen.Bounds and Window.Position live in.
    private static PixelPoint? Pointer() =>
        OperatingSystem.IsWindows() ? Win32Overlay.CursorPosition() : OperatingSystem.IsLinux() ? X11Pointer.Read()?.At : null;
}
