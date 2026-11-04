using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using ConditioningControlPanel.Input;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF GlobalHotkeyService.ChatHotkeyId. The registration owns only its own HWND slot.
internal sealed class Win32ChatHotkey : IDisposable
{
    private const int Slot = 0xB1B1;
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

    private Win32ChatHotkey(Window owner, IntPtr hwnd, Action pressed)
    {
        _owner = owner; _hwnd = hwnd; _pressed = pressed;
        Win32Properties.AddWndProcHookCallback(owner, OnMessage);
    }

    internal static IDisposable? Register(Window owner, Key key, KeyModifiers mods, Action pressed)
    {
        var handle = owner.TryGetPlatformHandle();
        if (!OperatingSystem.IsWindows() || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            || handle?.HandleDescriptor != "HWND" || handle.Handle == IntPtr.Zero) return null;
        var hwnd = handle.Handle;
        int vk = VirtualKeys.Of(key.ToString());
        uint flags = 0x4000 | ((mods & KeyModifiers.Alt) != 0 ? 1u : 0u)
            | ((mods & KeyModifiers.Control) != 0 ? 2u : 0u)
            | ((mods & KeyModifiers.Shift) != 0 ? 4u : 0u)
            | ((mods & KeyModifiers.Meta) != 0 ? 8u : 0u);
        if (vk == 0 || !RegisterHotKey(hwnd, Slot, flags, (uint)vk))
        {
            Log.Warning("Chat shortcut could not be registered; the in-window shortcut remains available");
            return null;
        }
        try { return new Win32ChatHotkey(owner, hwnd, pressed); }
        catch { UnregisterHotKey(hwnd, Slot); throw; }
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_disposed && message == 0x0312 && wParam.ToInt32() == Slot) { handled = true; _pressed(); }
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
