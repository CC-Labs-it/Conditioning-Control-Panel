using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The EMI Desk summon chord on X11 / XWayland, twin of WPF <c>GlobalHotkeyService</c> (RegisterHotKey):
/// an <c>XGrabKey</c> on the root, consumed like the Win32 hotkey; a combo another client holds fails
/// with BadAccess = "not armed". Reach: while an X11 client has focus. Own display; the listener
/// polls without <see cref="Gate"/> and drains under it, Arm/Disarm take it too (Xlib is not threaded).
/// Holds one grab per id (EMI summon, Quick Recal, camera start/stop), like WPF's per-slot RegisterHotKey.
/// </summary>
internal static class X11SummonChord
{
    private const string LibX11 = "libX11.so.6";
    private const int KeyPress = 2, MappingNotify = 34, BadAccess = 10, GrabModeAsync = 1;
    private const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    private const int XEventSize = 192, KeyEventState = 80, KeyEventKeycode = 84, ErrorEventCode = 32;
    private const uint ChordBits = ShiftMask | ControlMask | Mod1Mask | Mod4Mask;
    private static readonly uint[] LockExtras = { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short REvents; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern int XConnectionNumber(IntPtr display);
    [DllImport(LibX11)] private static extern int XPending(IntPtr display);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(LibX11)] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(LibX11)] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);
    [DllImport(LibX11)] private static extern int XSync(IntPtr display, bool discard);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);
    [DllImport(LibX11)] private static extern int XRefreshKeyboardMapping(IntPtr mappingEvent);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libc", SetLastError = true)] private static extern int poll([In, Out] PollFd[] fds, ulong nfds, int timeout);
    [DllImport("libc")] private static extern int pipe(int[] fds);
    [DllImport("libc")] private static extern nint read(int fd, byte[] buf, nint count);
    [DllImport("libc")] private static extern nint write(int fd, byte[] buf, nint count);
    private static readonly int[] _wake = { -1, -1 };   // self-pipe: Arm wakes the listener to drain what XSync queued

    private static readonly object Gate = new();
    private static IntPtr _display, _root;
    private static readonly Dictionary<string, (int Code, uint Mask, Action OnPress)> _grabs = new();
    private static bool _badAccess;
    // Rooted for the process lifetime: Xlib keeps the pointer while it is installed.
    private static readonly XErrorHandler Handler = OnXError;
    private static IntPtr _previousHandler;

    /// <summary>The EMI Desk summon chord (one grab, replaced on each Arm).</summary>
    internal static bool Arm(ChordMods mods, string key, Action onPress) => Arm("emi", mods, key, onPress);
    internal static void Disarm() => Disarm("emi");

    /// <summary>Grab under <paramref name="id"/> and return a handle that releases it, or null when refused.</summary>
    internal static IDisposable? Register(string id, ChordMods mods, string key, Action onPress)
        => Arm(id, mods, key, onPress) ? new Release(id) : null;

    private sealed class Release(string id) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) Disarm(id); }
    }

    /// <summary>Grab <paramref name="mods"/>+<paramref name="key"/> (a WPF/Avalonia Key name) under
    /// <paramref name="id"/> and run <paramref name="onPress"/> on the listener thread for each press.
    /// Replaces that id's earlier grab. False when there is no X display, the key has no keycode,
    /// another id of ours already holds the combo, or another client holds it.</summary>
    internal static bool Arm(string id, ChordMods mods, string key, Action onPress)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            lock (Gate)
            {
                if (!EnsureDisplay()) return false;
                UngrabLocked(id);
                int code = XKeysymToKeycode(_display, X11PanicKey.KeysymOf(key));
                if (code == 0) { Log.Warning("[EmiDesk] chord key '{Key}' has no X keycode", key); return false; }
                uint mask = ((mods & ChordMods.Ctrl) != 0 ? ControlMask : 0) | ((mods & ChordMods.Alt) != 0 ? Mod1Mask : 0)
                          | ((mods & ChordMods.Shift) != 0 ? ShiftMask : 0) | ((mods & ChordMods.Win) != 0 ? Mod4Mask : 0);
                // Same client re-grabbing a combo succeeds silently, so refuse a combo another id holds
                // here (WPF: RegisterHotKey fails for a second slot on the same chord).
                foreach (var g in _grabs.Values)
                    if (g.Code == code && g.Mask == mask) return false;

                // BadAccess arrives asynchronously, so catch it with a handler around an XSync. The
                // handler is process-wide; it forwards every error that is not ours to the previous one.
                _badAccess = false;
                _previousHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(Handler));
                // Caps Lock and Num Lock are modifiers to X: grab every combination of them too.
                foreach (var extra in LockExtras)
                    XGrabKey(_display, code, mask | extra, _root, false, GrabModeAsync, GrabModeAsync);
                XSync(_display, false);
                XSetErrorHandler(_previousHandler);

                if (_badAccess) { UngrabCombo(code, mask); return false; }
                _grabs[id] = (code, mask, onPress);
                // XSync read the socket, so anything it queued would sit until the next X traffic.
                if (_wake[1] >= 0) write(_wake[1], new byte[1], 1);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning("[EmiDesk] X11 chord unavailable ({Why})", ex.Message);
            return false;
        }
    }

    /// <summary>Release <paramref name="id"/>'s chord (feature switched off, chord refused). Safe to call when nothing is armed.</summary>
    internal static void Disarm(string id)
    {
        lock (Gate)
        {
            if (_display == IntPtr.Zero) return;
            try { UngrabLocked(id); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chord ungrab failed"); }
        }
    }

    private static void UngrabLocked(string id)
    {
        if (!_grabs.Remove(id, out var g)) return;
        UngrabCombo(g.Code, g.Mask);
    }

    // Only this combo's lock variants: AnyModifier would also drop another id's grab on the same key.
    private static void UngrabCombo(int code, uint mask)
    {
        foreach (var extra in LockExtras) XUngrabKey(_display, code, mask | extra, _root);
        XFlush(_display);
    }

    /// <summary>Which grab a key press belongs to: keycode plus the chord modifiers, Caps/Num Lock ignored.</summary>
    internal static Action? Match(IEnumerable<(int Code, uint Mask, Action OnPress)> grabs, int keycode, uint state)
    {
        foreach (var g in grabs)
            if (g.Code == keycode && (state & ChordBits) == g.Mask) return g.OnPress;
        return null;
    }

    private static bool EnsureDisplay()
    {
        if (_display != IntPtr.Zero) return true;
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero) { Log.Warning("[EmiDesk] no X display: the summon chord cannot be armed"); return false; }
        _root = XDefaultRootWindow(_display);
        var fd = XConnectionNumber(_display);
        pipe(_wake);
        new Thread(() => Listen(fd)) { IsBackground = true, Name = "emi-chord-x11" }.Start();
        return true;
    }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        if (display == _display)
        {
            if (Marshal.ReadByte(errorEvent, ErrorEventCode) == BadAccess) _badAccess = true;
            return 0;
        }
        return _previousHandler == IntPtr.Zero ? 0
            : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(_previousHandler)(display, errorEvent);
    }

    private static void Listen(int fd)
    {
        var ev = Marshal.AllocHGlobal(XEventSize);
        var pfd = new[] { new PollFd { Fd = fd, Events = 1 /* POLLIN */ }, new PollFd { Fd = _wake[0], Events = 1 } };
        var buf = new byte[16];
        while (true)
        {
            try
            {
                // Block without the lock on the X socket and the wake pipe; drain the queue on every wake.
                if (poll(pfd, _wake[0] >= 0 ? 2UL : 1UL, -1) < 0 && Marshal.GetLastWin32Error() != 4 /* EINTR */) Thread.Sleep(1000);
                if ((pfd[1].REvents & 1) != 0) read(_wake[0], buf, buf.Length);
                Action? fire = null;
                lock (Gate)
                {
                    while (XPending(_display) > 0)
                    {
                        XNextEvent(_display, ev);
                        int type = Marshal.ReadInt32(ev);
                        if (type == MappingNotify) XRefreshKeyboardMapping(ev);
                        else if (type == KeyPress
                                 && Match(_grabs.Values, Marshal.ReadInt32(ev, KeyEventKeycode), (uint)Marshal.ReadInt32(ev, KeyEventState)) is { } hit)
                            fire += hit;
                    }
                }
                fire?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] chord listener event failed");
            }
        }
    }
}
