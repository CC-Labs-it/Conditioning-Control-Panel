using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Input;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF GlobalHotkeyService.CameraHotkeyId. The shell owns the native registration.
internal sealed class CameraGlobalHotkey : IDisposable
{
    private const int Slot = 0xB1B2;
    private readonly Window _owner;
    private readonly IntPtr _hwnd;
    private readonly Action _pressed;
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private CameraGlobalHotkey(Window owner, IntPtr hwnd, Action pressed)
    {
        _owner = owner; _hwnd = hwnd; _pressed = pressed;
        Win32Properties.AddWndProcHookCallback(owner, OnMessage);
    }

    internal static IDisposable? Register(Window owner, string key, uint modifiers, Action pressed)
    {
        var vk = VirtualKeys.Of(key);
        if (!OperatingSystem.IsWindows() || vk == 0 || owner.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return null;
        if (!RegisterHotKey(hwnd, Slot, modifiers | 0x4000, (uint)vk))
        {
            Log.Warning("Camera hotkey registration failed (Win32 {Error})", Marshal.GetLastWin32Error());
            return null;
        }
        return new CameraGlobalHotkey(owner, hwnd, pressed);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_hwnd, Slot);
        Win32Properties.RemoveWndProcHookCallback(_owner, OnMessage);
    }
}
