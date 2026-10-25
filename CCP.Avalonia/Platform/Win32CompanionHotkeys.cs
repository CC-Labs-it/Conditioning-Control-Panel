using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Input;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalHotkeyService's two companion slots. The shell owns the message hook;
/// failed registrations leave the in-window bindings intact. All calls run on the UI thread.</summary>
internal sealed class Win32CompanionHotkeys : IDisposable
{
    internal const int Chat = 0xB1B1, Camera = 0xB1B2;
    private readonly IntPtr _hwnd;
    private readonly Func<IntPtr, int, uint, uint, bool> _register;
    private readonly Action<IntPtr, int> _unregister;
    private readonly Dictionary<int, Action> _callbacks = new();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    internal Win32CompanionHotkeys(IntPtr hwnd,
        Func<IntPtr, int, uint, uint, bool> register, Action<IntPtr, int> unregister)
    {
        _hwnd = hwnd; _register = register; _unregister = unregister;
    }

    internal static Win32CompanionHotkeys? Attach(Window owner)
    {
        if (!OperatingSystem.IsWindows() || owner.TryGetPlatformHandle() is not { Handle: var hwnd }
            || hwnd == IntPtr.Zero) return null;
        var service = new Win32CompanionHotkeys(hwnd, RegisterHotKey, (h, id) => UnregisterHotKey(h, id));
        Win32Properties.AddWndProcHookCallback(owner, (IntPtr h, uint msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x0312 && service.Dispatch(w.ToInt32())) handled = true;
            return IntPtr.Zero;
        });
        return service;
    }

    internal static uint Modifiers(KeyModifiers mods) => 0x4000u // MOD_NOREPEAT, as WPF
        | (mods.HasFlag(KeyModifiers.Alt) ? 1u : 0)
        | (mods.HasFlag(KeyModifiers.Control) ? 2u : 0)
        | (mods.HasFlag(KeyModifiers.Shift) ? 4u : 0)
        | (mods.HasFlag(KeyModifiers.Meta) ? 8u : 0);

    internal bool Set(int id, bool enabled, Key key, KeyModifiers mods, Action callback)
    {
        Remove(id); // WPF drops the old chord even when the replacement is refused.
        if (!enabled) return false;
        var vk = VirtualKeys.Of(key.ToString());
        if (vk == 0 || !_register(_hwnd, id, Modifiers(mods), (uint)vk))
        {
            Log.Information("Companion global shortcut registration refused for slot {Slot}; in-window binding remains", id);
            return false;
        }
        _callbacks[id] = callback;
        return true;
    }

    internal bool Dispatch(int id)
    {
        if (!_callbacks.TryGetValue(id, out var callback)) return false;
        Dispatcher.UIThread.Post(callback);
        return true;
    }

    private void Remove(int id)
    {
        if (_callbacks.Remove(id)) _unregister(_hwnd, id);
    }

    public void Dispose()
    {
        Remove(Chat); Remove(Camera);
    }
}
