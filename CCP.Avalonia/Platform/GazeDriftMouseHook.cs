using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalMouseHook's non-consuming left-down path. Installed on the UI thread.</summary>
internal sealed class GazeDriftMouseHook : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? name);
    private readonly Action<int, int> _click;
    private readonly HookProc _proc;
    private IntPtr _hook;

    private GazeDriftMouseHook(Action<int, int> click) { _click = click; _proc = Callback; }
    private static bool _failed;
    internal static bool Available() => !_failed && (OperatingSystem.IsWindows() || X11GazeDriftMouseHook.Available());
    internal static IDisposable? Start(Action<int, int> click)
    {
        if (!OperatingSystem.IsWindows())
        {
            var listener = X11GazeDriftMouseHook.Start(click);
            _failed = listener == null;
            return listener;
        }
        var hook = new GazeDriftMouseHook(click);
        hook._hook = SetWindowsHookEx(14, hook._proc, GetModuleHandle(null), 0);
        _failed = hook._hook == IntPtr.Zero;
        return _failed ? null : hook;
    }
    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message == (IntPtr)0x0201)
        {
            try { _click(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)); } catch { }
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
