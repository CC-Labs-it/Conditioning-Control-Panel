using System;
using ConditioningControlPanel.Input;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF GlobalHotkeyService.CameraHotkeyId: independent RegisterHotKey message pump.
/// A refused registration leaves the shell's in-window binding active.</summary>
internal sealed class Win32CameraChord
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private Thread? _thread;
    private uint _threadId;

    internal bool Arm(ChordMods mods, string key, Action onPress)
    {
        Disarm();
        var vk = VirtualKeys.Of(key);
        if (!OperatingSystem.IsWindows() || vk == 0) return false;
        bool armed = false;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // create queue before the caller can Disarm
            uint mask = ((mods & ChordMods.Alt) != 0 ? 1u : 0) | ((mods & ChordMods.Ctrl) != 0 ? 2u : 0)
                | ((mods & ChordMods.Shift) != 0 ? 4u : 0) | ((mods & ChordMods.Win) != 0 ? 8u : 0);
            armed = RegisterHotKey(IntPtr.Zero, 1, mask | 0x4000 /* MOD_NOREPEAT */, (uint)vk);
            ready.Set();
            if (!armed) return;
            try
            {
                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                    if (msg.Id == 0x0312 /* WM_HOTKEY */) onPress();
            }
            finally { UnregisterHotKey(IntPtr.Zero, 1); }
        }) { IsBackground = true, Name = "camera-hotkey" };
        _thread.Start();
        ready.Wait();
        return armed;
    }

    internal void Disarm()
    {
        if (_thread == null) return;
        if (_thread.IsAlive)
        {
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join();
        }
        _thread = null;
        _threadId = 0;
    }
}
