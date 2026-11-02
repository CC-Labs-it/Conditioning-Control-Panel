using System;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel.Services.Browser;

/// <summary>
/// A window that only carries a WebView2 (the browser popout and both fullscreen hosts) paints
/// nothing of its own: its whole client area is the WebView2 child HWND, which Chromium draws in
/// its own GPU process. WPF still gave it a hardware render target the size of the window, so a
/// fullscreen or maximize asked the app's ONE render thread for a fresh monitor-sized D3D surface
/// at the very moment Chromium takes video memory for fullscreen playback. When that allocation
/// fails the render thread dies for every window and the app has to exit
/// (UCEERR_RENDERTHREADFAILURE from HwndTarget.UpdateWindowPos, ccp-bugs #1504, #1510, #1513).
/// A software target fills one black rectangle and needs no video memory at all.
/// </summary>
internal static class BrowserHostSurface
{
    /// <summary>Call right after constructing the window, before or after Show.</summary>
    internal static void UseSoftwareTarget(Window? window)
    {
        if (window == null) return;
        if (!TryApply(window))
            window.SourceInitialized += (_, _) => TryApply(window);
    }

    /// <summary>False only when the window has no HWND yet.</summary>
    private static bool TryApply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || HwndSource.FromHwnd(hwnd) is not { CompositionTarget: not null } source)
                return false;
            source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Browser host software target failed: {Error}", ex.Message);
            return true;
        }
    }
}
