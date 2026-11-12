using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF VideoService.cs:1392-1545: leased playback has its own ladder and cached handles.
internal sealed class BubbleCountWedgeWatchdog
{
    internal static readonly BubbleCountWedgeWatchdog Game = new();
    private readonly object _gate = new();
    private readonly HashSet<IntPtr> _windows = new();
    private readonly Func<long> _clock;
    private readonly Action<IntPtr[]> _release;
    private readonly Action _stop;
    private Timer? _timer;
    private DispatcherTimer? _heartbeat;
    private bool _armed;
    private long _lastBeat;
    private int _rung;

    internal BubbleCountWedgeWatchdog(Func<long>? clock = null, Action<IntPtr[]>? release = null, Action? stop = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
        _release = release ?? Release;
        _stop = stop ?? BubbleCountWindow.StopForWedge;
    }

    internal void Arm(bool startTimers = true)
    {
        Disarm();
        lock (_gate) { _armed = true; _lastBeat = _clock(); _rung = 0; }
        if (!startTimers) return;
        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeat.Tick += (_, _) => Beat();
        _heartbeat.Start();
        _timer = new Timer(_ => Tick(), null, 3000, 3000);
    }

    internal void Beat() { lock (_gate) { if (_armed) _lastBeat = _clock(); } }
    internal void Register(IntPtr id) { lock (_gate) { if (_armed && id != IntPtr.Zero) _windows.Add(id); } }
    internal void Unregister(IntPtr id) { lock (_gate) _windows.Remove(id); }
    internal bool IsArmed { get { lock (_gate) return _armed; } }
    internal void Tick()
    {
        IntPtr[]? ids = null;
        lock (_gate)
        {
            if (!_armed) return;
            var step = VideoWedgeLadder.NextRung(_clock() - _lastBeat, _rung, true, false);
            if (step == VideoWedgeLadder.WedgeAction.StopPlayers)
            {
                _rung = 1;
                try { _stop(); } catch (Exception ex) { Serilog.Log.Debug(ex, "Bubble count wedge stop failed"); }
            }
            else if (step == VideoWedgeLadder.WedgeAction.EscapeHatch)
            {
                _rung = 3;
                ids = new IntPtr[_windows.Count];
                _windows.CopyTo(ids);
            }
        }
        if (ids != null) _release(ids);
    }
    internal void Disarm()
    {
        lock (_gate) { _armed = false; _windows.Clear(); }
        _timer?.Dispose(); _timer = null;
        _heartbeat?.Stop(); _heartbeat = null;
    }

    internal static bool SupportsHandle(IPlatformHandle? handle) =>
        X11Overlay.BackendOf(handle) is OverlayBackend.X11 or OverlayBackend.Win32;

    internal bool RegisterHandle(IPlatformHandle? handle)
    {
        if (!SupportsHandle(handle)) return false;
        // Install the existing process-wide X error handler before stale XIDs can be encountered.
        if (X11Overlay.BackendOf(handle) == OverlayBackend.X11) _ = X11Overlay.IsAvailable;
        Register(handle!.Handle);
        return true;
    }

    internal void Register(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (!RegisterHandle(handle)) return;
        var id = handle!.Handle;
        window.Closed += (_, _) => Unregister(id);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

    private static void Release(IntPtr[] ids) =>
        Release(ids, OperatingSystem.IsWindows(), ReleaseWindowsAsync, ReleaseX11);

    internal static void Release(IntPtr[] ids, bool windows, Action<IntPtr[]> releaseWindows, Action<IntPtr[]> releaseX11)
    {
        if (windows) releaseWindows(ids); else releaseX11(ids);
    }

    // A blocked window cannot hold up the timer or another window's release.
    private static void ReleaseWindowsAsync(IntPtr[] ids)
    {
        foreach (var id in ids)
            _ = Task.Run(() => ReleaseWindows(new[] { id },
                (window, after, flags) => SetWindowPos(window, after, 0, 0, 0, 0, flags),
                (window, command) => ShowWindow(window, command)));
    }

    internal static void ReleaseWindows(IntPtr[] ids, Action<IntPtr, IntPtr, uint> removeTopmost, Action<IntPtr, int> hide)
    {
        const uint flags = 0x4000 | 0x10 | 0x2 | 0x1; // async, no activate, no move, no size
        foreach (var id in ids)
        {
            try { removeTopmost(id, new IntPtr(-2), flags); hide(id, 0); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Bubble count window release failed"); }
        }
    }

    private const string X11 = "libX11.so.6";
    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(X11)] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long mask, IntPtr data);
    [DllImport(X11)] private static extern int XUnmapWindow(IntPtr display, IntPtr window);
    [DllImport(X11)] private static extern int XSync(IntPtr display, bool discard);

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientMessage
    {
        internal int Type;
        internal nuint Serial;
        internal int SendEvent;
        internal IntPtr Display, Window, MessageType;
        internal int Format;
        internal nint Action, First, Second, Source, Unused;
    }

    private static void ReleaseX11(IntPtr[] ids)
    {
        if (!OperatingSystem.IsLinux() || ids.Length == 0) return;
        IntPtr display = IntPtr.Zero, ev = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;
            var root = XDefaultRootWindow(display);
            var state = XInternAtom(display, "_NET_WM_STATE", false);
            var above = XInternAtom(display, "_NET_WM_STATE_ABOVE", false);
            ev = Marshal.AllocHGlobal(192); // sizeof(XEvent)
            foreach (var id in ids)
            {
                var message = new ClientMessage { Type = 33, Display = display, Window = id,
                    MessageType = state, Format = 32, Action = 0, First = above, Source = 2 };
                Marshal.StructureToPtr(message, ev, false);
                XSendEvent(display, root, false, 1572864, ev);
                XUnmapWindow(display, id);
            }
            XSync(display, false);
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Bubble count wedge escape failed"); }
        finally { if (ev != IntPtr.Zero) Marshal.FreeHGlobal(ev); if (display != IntPtr.Zero) XCloseDisplay(display); }
    }
}
