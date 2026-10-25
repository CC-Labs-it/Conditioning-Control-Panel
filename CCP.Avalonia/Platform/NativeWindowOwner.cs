using System;
using System.Runtime.InteropServices;
using Serilog;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF ChaosWebViewHost.AttachMainWindowGlue: native ownership only, without managed
/// visibility or modal coupling. The link is suspended while the panel is hidden/minimized.</summary>
internal sealed class NativeWindowOwner : IDisposable
{
    private readonly Window _window, _owner;
    private bool _suspended, _disposed;
    internal bool IsLinked { get; private set; }

    internal NativeWindowOwner(Window window, Window owner)
    {
        _window = window; _owner = owner;
        owner.PropertyChanged += OwnerChanged;
        window.Closed += WindowClosed;
        if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(window, CascadeHide);
        Refresh();
    }

    internal void Suspend(bool suspend) { _suspended = suspend; Refresh(); }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty && e.Property != Visual.IsVisibleProperty) return;
        Refresh();
        // WPF's deferred repair: the WM_SIZE notification may land after the owner-hide cascade.
        if (!IsLinked) Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && _window.IsVisible) RepairVisibility();
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        if (_disposed) return;
        bool link = !_suspended && _owner.IsVisible && _owner.WindowState != WindowState.Minimized;
        if (link == IsLinked) return;
        Apply(link);
        IsLinked = link;
    }

    private IntPtr CascadeHide(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_SHOWWINDOW / SW_PARENTCLOSING: sever BEFORE USER32 hides the owned player.
        if (msg == 0x18 && wParam == IntPtr.Zero && lParam.ToInt64() == 1)
        {
            Apply(false);
            IsLinked = false;
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Apply(bool linked)
    {
        var child = _window.TryGetPlatformHandle();
        var parent = _owner.TryGetPlatformHandle();
        if (child is null || parent is null) return;
        try
        {
            if (OperatingSystem.IsWindows() && child.HandleDescriptor == "HWND")
            {
                SetOwner(child.Handle, linked ? parent.Handle : IntPtr.Zero);
                // WPF keeps the player's own taskbar entry, even while natively owned.
                SetWindowLong(child.Handle, -20, GetWindowLong(child.Handle, -20) | 0x40000);
                SetWindowPos(child.Handle, IntPtr.Zero, 0, 0, 0, 0, linked ? 0x33u : 0x37u);
            }
            else if (OperatingSystem.IsLinux() && child.HandleDescriptor == "XID")
                X11(child.Handle, linked ? parent.Handle : IntPtr.Zero, false);
        }
        catch (Exception ex) { Log.Debug("Just Drop native owner: {Kind}", ex.GetType().Name); }
    }

    private void RepairVisibility()
    {
        var handle = _window.TryGetPlatformHandle();
        if (handle is null) return;
        try
        {
            if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND") ShowWindow(handle.Handle, 4);
            else if (OperatingSystem.IsLinux() && handle.HandleDescriptor == "XID") X11(handle.Handle, IntPtr.Zero, true);
        }
        catch (Exception ex) { Log.Debug("Just Drop visibility repair: {Kind}", ex.GetType().Name); }
    }

    // OS-specific calls stay in this small seam; no managed Window.Owner visibility coupling.
    private static void SetOwner(IntPtr child, IntPtr parent)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(child, -8, parent);
        else SetWindowLong(child, -8, unchecked((uint)parent.ToInt32()));
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr w, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern uint SetWindowLong(IntPtr w, int index, uint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint GetWindowLong(IntPtr w, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr w, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr w, int command);

    private static void X11(IntPtr child, IntPtr parent, bool show)
    {
        var c = xcb_connect(IntPtr.Zero, out _);
        if (c == IntPtr.Zero) return;
        try
        {
            if (xcb_connection_has_error(c) != 0) return;
            uint w = unchecked((uint)child.ToInt64()), owner = unchecked((uint)parent.ToInt64());
            // ICCCM predefined atoms: WM_TRANSIENT_FOR = 68, WINDOW = 33. Checked requests
            // tolerate a window disappearing without Xlib's process-ending BadWindow handler.
            uint cookie = show ? xcb_map_window_checked(c, w)
                : owner == 0 ? xcb_delete_property_checked(c, w, 68)
                : xcb_change_property_checked(c, 0, w, 68, 33, 32, 1, ref owner);
            var error = xcb_request_check(c, cookie);
            if (error != IntPtr.Zero) free(error);
            xcb_flush(c);
        }
        finally { xcb_disconnect(c); }
    }
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_connect(IntPtr display, out int screen);
    [DllImport("libxcb.so.1")] private static extern int xcb_connection_has_error(IntPtr c);
    [DllImport("libxcb.so.1")] private static extern uint xcb_change_property_checked(IntPtr c, byte mode, uint w, uint property, uint type, byte format, uint count, ref uint data);
    [DllImport("libxcb.so.1")] private static extern uint xcb_delete_property_checked(IntPtr c, uint w, uint property);
    [DllImport("libxcb.so.1")] private static extern uint xcb_map_window_checked(IntPtr c, uint w);
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_request_check(IntPtr c, uint cookie);
    [DllImport("libxcb.so.1")] private static extern int xcb_flush(IntPtr c);
    [DllImport("libxcb.so.1")] private static extern void xcb_disconnect(IntPtr c);
    [DllImport("libc")] private static extern void free(IntPtr p);

    private void WindowClosed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.PropertyChanged -= OwnerChanged;
        _window.Closed -= WindowClosed;
        if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(_window, CascadeHide);
        IsLinked = false;
    }
}
