using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Native ownership without managed visibility coupling (WPF GWL_HWNDPARENT).</summary>
internal static class NativeWindowOwner
{
    // Headless tests record ownership requests; they must never touch the real desktop.
    internal static Func<Window, Window?, bool>? SetForTests;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    internal static bool Set(Window window, Window? owner)
    {
        if (SetForTests is { } test) return test(window, owner);
        var handle = window.TryGetPlatformHandle();
        try
        {
            if (X11Overlay.BackendOf(handle) != OverlayBackend.Win32)
                return X11Overlay.SetTransientOwner(window, owner);
            var parent = owner?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            Marshal.SetLastPInvokeError(0);
            if (IntPtr.Size == 8) SetWindowLongPtr(handle!.Handle, -8, parent); // GWL_HWNDPARENT
            else SetWindowLong(handle!.Handle, -8, parent.ToInt32());
            if (Marshal.GetLastPInvokeError() != 0) return false;
            // WPF WS_EX_APPWINDOW: ownership must not take away the player's taskbar entry.
            SetWindowLong(handle.Handle, -20, (GetWindowLong(handle.Handle, -20) | 0x40000) & ~0x80);
            SetWindowPos(handle.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37); // frame changed, no move/size/z/activation
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Native owner unavailable: {Kind}", ex.GetType().Name);
            return false;
        }
    }

    internal static void RepairVisibility(Window window)
    {
        if (SetForTests is not null) return;
        var handle = window.TryGetPlatformHandle();
        if (X11Overlay.BackendOf(handle) == OverlayBackend.Win32) ShowWindow(handle!.Handle, 8); // SW_SHOWNA
        else X11Overlay.MapOwnedWindow(window);
    }
}
