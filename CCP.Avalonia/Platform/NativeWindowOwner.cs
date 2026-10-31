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
    internal static Func<Window, bool>? NativeVisibleForTests;
    internal static Action<Window>? ShowForTests;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
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
            // WPF EnsureTaskbarButton (ChaosWebViewHost.cs:1013,1831): re-owning a SHOWN window drops its button.
            if (OperatingSystem.IsWindows()) AddTaskbarButton(handle.Handle);
            if (parent != IntPtr.Zero)
            {
                // WPF ApplyNativeOwner (ChaosWebViewHost.cs:1041): ownership rules the future, not the present;
                // slot above the panel now (no move/size/activation, no owner reorder), keeping Topmost.
                SetWindowPos(handle.Handle, parent, 0, 0, 0, 0, 0x213);
                if (window.Topmost) SetWindowPos(handle.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213); // HWND_TOPMOST
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Native owner unavailable: {Kind}", ex.GetType().Name);
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddTaskbarButton(IntPtr hwnd)
    {
        try
        {
            var list = (ITaskbarList)new TaskbarListCoClass();
            list.HrInit();
            list.AddTab(hwnd);
        }
        catch (Exception ex) { Log.Debug("Taskbar button unavailable: {Kind}", ex.GetType().Name); }
    }

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarListCoClass { }

    internal static void RepairVisibility(Window window)
    {
        // WPF ChaosWebViewHost.cs:1106-1110: deliberate hide/minimize is not a cascade to heal.
        if (!window.IsVisible || window.WindowState == WindowState.Minimized) return;
        var handle = window.TryGetPlatformHandle();
        bool win32 = X11Overlay.BackendOf(handle) == OverlayBackend.Win32;
        bool nativeVisible = NativeVisibleForTests?.Invoke(window)
            ?? (win32 && IsWindowVisible(handle!.Handle));
        if (nativeVisible) return;
        if (ShowForTests is { } show) { show(window); return; }
        if (SetForTests is not null) return;
        if (win32) ShowWindow(handle!.Handle, 8); // SW_SHOWNA
        else X11Overlay.MapOwnedWindow(window);
    }
}
