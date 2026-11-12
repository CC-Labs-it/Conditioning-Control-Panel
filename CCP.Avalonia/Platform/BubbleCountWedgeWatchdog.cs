using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF VideoService.cs:1392-1545: leased playback has its own ladder and cached handles.
internal sealed class BubbleCountWedgeWatchdog
{
    internal static readonly BubbleCountWedgeWatchdog Game = new();
    private readonly object _gate = new();
    private readonly HashSet<IntPtr> _windows = new();
    private readonly Func<long> _clock;
    private readonly Action<IntPtr[]> _release;
    private Timer? _timer;
    private DispatcherTimer? _heartbeat;
    private bool _armed;
    private long _lastBeat;
    private int _rung;

    internal BubbleCountWedgeWatchdog(Func<long>? clock = null, Action<IntPtr[]>? release = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
        _release = release ?? ReleaseX11;
    }

    internal void Arm(bool startTimers = true)
    {
        Disarm();
        lock (_gate) { _armed = true; _lastBeat = _clock(); _rung = 1; }
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
            if (VideoWedgeLadder.NextRung(_clock() - _lastBeat, _rung, true, false) == VideoWedgeLadder.WedgeAction.EscapeHatch)
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

    internal void Register(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (X11Overlay.BackendOf(handle) != OverlayBackend.X11) return;
        // Install the existing process-wide X error handler before stale XIDs can be encountered.
        _ = X11Overlay.IsAvailable;
        var id = handle!.Handle;
        Register(id);
        window.Closed += (_, _) => Unregister(id);
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
