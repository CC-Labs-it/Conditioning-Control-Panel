using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Input;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalHotkeyService camera and Quick Recal slots, owned by the shell HWND.</summary>
internal sealed class Win32QuickRecalHotkey : IDisposable
{
    private const int Slot = 0xB1B3;
    private readonly int _slot;
    private readonly Window _owner;
    private readonly IntPtr _hwnd;
    private readonly Action _pressed;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private Win32QuickRecalHotkey(Window owner, IntPtr hwnd, int slot, Action pressed)
    {
        _slot = slot;
        _owner = owner;
        _hwnd = hwnd;
        _pressed = pressed;
        Win32Properties.AddWndProcHookCallback(owner, OnMessage);
    }

    internal static IDisposable? Register(Window owner, Action pressed)
        => Register(owner, Slot, 0x4003, 0x47, pressed);

    internal static IDisposable? RegisterCamera(Window owner, Key key, KeyModifiers mods, Action pressed)
    {
        int vk = VirtualKeys.Of(key.ToString());
        uint flags = 0x4000 | ((mods & KeyModifiers.Alt) != 0 ? 1u : 0)
            | ((mods & KeyModifiers.Control) != 0 ? 2u : 0)
            | ((mods & KeyModifiers.Shift) != 0 ? 4u : 0)
            | ((mods & KeyModifiers.Meta) != 0 ? 8u : 0);
        return vk == 0 ? null : Register(owner, 0xB1B2, flags, (uint)vk, pressed);
    }

    private static IDisposable? Register(Window owner, int slot, uint modifiers, uint key, Action pressed)
    {
        if (!OperatingSystem.IsWindows() || owner.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return null;
        if (!RegisterHotKey(hwnd, slot, modifiers, key))
        {
            Log.Warning("Hotkey could not be registered (Win32 {Error}); in-app controls remain available",
                Marshal.GetLastWin32Error());
            return null;
        }
        return new Win32QuickRecalHotkey(owner, hwnd, slot, pressed);
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_disposed && message == 0x0312 && wParam.ToInt32() == _slot)
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
        UnregisterHotKey(_hwnd, _slot);
        Win32Properties.RemoveWndProcHookCallback(_owner, OnMessage);
    }
}
