using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Input;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalHotkeyService's 0xB1B3 (Quick Recal) and 0xB1B2 (camera start/stop) slots, owned by
/// the shell HWND. No native calls off Windows.</summary>
internal sealed class Win32QuickRecalHotkey : IDisposable
{
    private const int QuickRecalSlot = 0xB1B3;
    internal const int CameraSlot = 0xB1B2;
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

    // MOD_NOREPEAT | MOD_CONTROL | MOD_ALT, VK_G: same chord and slot as WPF.
    internal static IDisposable? Register(Window owner, Action pressed) => Register(owner, QuickRecalSlot, 0x4003, 0x47, pressed);

    /// <summary>The camera start/stop chord in WPF's camera slot (GlobalHotkeyService.Register: MOD_NOREPEAT added).</summary>
    internal static IDisposable? RegisterCamera(Window owner, ChordMods mods, string key, Action pressed)
    {
        uint m = 0x4000 | ((mods & ChordMods.Alt) != 0 ? 1u : 0) | ((mods & ChordMods.Ctrl) != 0 ? 2u : 0)
               | ((mods & ChordMods.Shift) != 0 ? 4u : 0) | ((mods & ChordMods.Win) != 0 ? 8u : 0);
        int vk = VirtualKeys.Of(key);
        return vk == 0 ? null : Register(owner, CameraSlot, m, (uint)vk, pressed);
    }

    private static IDisposable? Register(Window owner, int slot, uint mods, uint vk, Action pressed)
    {
        if (!OperatingSystem.IsWindows() || owner.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
            return null;
        if (!RegisterHotKey(hwnd, slot, mods, vk))
        {
            Log.Warning("Global hotkey slot {Slot:X} could not be registered (Win32 {Error}); in-app buttons remain available",
                slot, Marshal.GetLastWin32Error());
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
