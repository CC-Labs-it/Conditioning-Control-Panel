using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalHotkeyService's 0xB1B3 slot, owned by the shell HWND. No native calls off Windows.</summary>
internal sealed class Win32QuickRecalHotkey : IDisposable
{
    private const int Slot = 0xB1B3;
    private readonly Window _owner;
    private readonly IntPtr _hwnd;
    private readonly Action _pressed;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private Win32QuickRecalHotkey(Window owner, IntPtr hwnd, Action pressed)
    {
        _owner = owner;
        _hwnd = hwnd;
        _pressed = pressed;
        Win32Properties.AddWndProcHookCallback(owner, OnMessage);
    }

    internal static IDisposable? Register(Window owner, Action pressed)
    {
        if (!OperatingSystem.IsWindows() || owner.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return null;
        // MOD_NOREPEAT | MOD_CONTROL | MOD_ALT, VK_G: same chord and slot as WPF.
        if (!RegisterHotKey(hwnd, Slot, 0x4003, 0x47))
        {
            Log.Warning("Quick Recal Ctrl+Alt+G could not be registered (Win32 {Error}); in-app buttons remain available",
                Marshal.GetLastWin32Error());
            return null;
        }
        return new Win32QuickRecalHotkey(owner, hwnd, pressed);
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_disposed && message == 0x0312 && wParam.ToInt32() == Slot)
        {
            handled = true;
            _pressed();
        }
        return IntPtr.Zero;
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_hwnd, Slot);
        Win32Properties.RemoveWndProcHookCallback(_owner, OnMessage);
    }
}
