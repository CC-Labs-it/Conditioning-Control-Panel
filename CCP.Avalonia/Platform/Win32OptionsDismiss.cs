using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF EmiOptionsWindow's non-consuming click-away / Escape hooks. Owned only while
/// the panel is open, on Avalonia's message-pumping UI thread. No key or pointer history is kept.</summary>
internal sealed class Win32OptionsDismiss : IDisposable
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern uint GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern uint SetWindowLong(IntPtr hwnd, int index, uint value);

    // WPF Windows/EmiDesk/EmiOptionsWindow.xaml.cs:108-114: tool window, no activation.
    internal static uint PassiveStyle(uint extendedStyle)
        => extendedStyle | Win32Overlay.WsExToolWindow | Win32Overlay.WsExNoActivate;

    internal static void PreventActivation(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;
        // Avalonia rebuilds styles when decorations/taskbar properties change.
        Win32Properties.AddWindowStylesCallback(window, (style, ex) => (style, PassiveStyle(ex)));
        SetWindowLong(handle.Handle, -20, PassiveStyle(GetWindowLong(handle.Handle, -20)));
    }

    private const int MouseHook = 14, KeyboardHook = 13;
    private const int LeftDown = 0x0201, RightDown = 0x0204, KeyDown = 0x0100, SysKeyDown = 0x0104;
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);

    // Delegates remain rooted until both hooks are removed.
    private readonly HookProc _mouseProc, _keyProc;
    private readonly Action<PixelPoint?, bool> _onPress;
    private IntPtr _mouse, _keys;
    private bool _disposed;

    private Win32OptionsDismiss(Action<PixelPoint?, bool> onPress)
    {
        _onPress = onPress;
        _mouseProc = OnMouse;
        _keyProc = OnKey;
    }

    internal static IDisposable? Start(Action<PixelPoint?, bool> onPress)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var listener = new Win32OptionsDismiss(onPress);
        try
        {
            var module = GetModuleHandle(null);
            listener._mouse = SetWindowsHookEx(MouseHook, listener._mouseProc, module, 0);
            listener._keys = SetWindowsHookEx(KeyboardHook, listener._keyProc, module, 0);
            if (listener._mouse != IntPtr.Zero && listener._keys != IntPtr.Zero) return listener;
            Log.Warning("[EmiDesk] options dismissal hook install failed ({Code})", Marshal.GetLastWin32Error());
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] options dismissal hook install failed"); }
        listener.Dispose();
        return null;
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (!_disposed && code >= 0 && (int)message is LeftDown or RightDown)
                _onPress(new PixelPoint(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)), false);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options mouse hook callback failed"); }
        // Always pass through: folding must not cost the click aimed at another application.
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (!_disposed && code >= 0 && (int)message is KeyDown or SysKeyDown && Marshal.ReadInt32(data) == 0x1b)
                _onPress(null, true);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options key hook callback failed"); }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mouse != IntPtr.Zero) { UnhookWindowsHookEx(_mouse); _mouse = IntPtr.Zero; }
        if (_keys != IntPtr.Zero) { UnhookWindowsHookEx(_keys); _keys = IntPtr.Zero; }
    }
}
