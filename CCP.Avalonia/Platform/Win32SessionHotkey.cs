using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Input;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalHotkeyService: one thread-owned RegisterHotKey, disposed on rebind/close.
/// ponytail: other desktops retain the in-window binding; no portal/X11 registration here.</summary>
internal sealed class Win32SessionHotkey : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint mods, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint w, nint l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private volatile uint _thread;
    private readonly Thread _listener;
    private volatile bool _disposed;
    internal static IDisposable? Bind(Key key, KeyModifiers mods, Action press) =>
        OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("CCP_USERDATA_DIR") == null ? new Win32SessionHotkey(key, mods, press) : null;
    private Win32SessionHotkey(Key key, KeyModifiers mods, Action press)
    {
        using var ready = new ManualResetEventSlim();
        _listener = new Thread(() =>
        {
            _thread = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0); // create queue before Dispose may post WM_QUIT
            uint mask = 0x4000u | ((mods & KeyModifiers.Alt) != 0 ? 1u : 0)
                | ((mods & KeyModifiers.Control) != 0 ? 2u : 0) | ((mods & KeyModifiers.Shift) != 0 ? 4u : 0)
                | ((mods & KeyModifiers.Meta) != 0 ? 8u : 0);
            bool armed = RegisterHotKey(0, 1, mask, (uint)VirtualKeys.Of(key.ToString()));
            if (!armed) _thread = 0; // a finished listener's thread id may be reused by Windows
            ready.Set();
            if (!armed) return; // WPF silently falls back when the OS refuses the combination
            try
            {
                while (GetMessage(out var message, 0, 0, 0) > 0)
                    if (message.Id == 0x312) Dispatcher.UIThread.Post(() => { if (!_disposed) press(); });
            }
            finally { UnregisterHotKey(0, 1); _thread = 0; }
        }) { IsBackground = true, Name = "session-hotkey" };
        _listener.Start();
        ready.Wait();
    }
    public void Dispose() { _disposed = true; if (_thread != 0) PostThreadMessage(_thread, 0x12, 0, 0); _listener.Join(); }
}
