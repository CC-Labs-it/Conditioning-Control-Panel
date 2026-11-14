using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Linux foreground source for <c>WindowAwarenessService</c> (WPF: user32 GetForegroundWindow +
/// GetWindowText, App.xaml.cs ReadForegroundWindowTitle). Reads the EWMH root property
/// <c>_NET_ACTIVE_WINDOW</c>, then that window's <c>_NET_WM_NAME</c> (UTF-8), falling back to
/// <c>WM_NAME</c>, and the same window's <c>_NET_WM_PID</c> when available.
///
/// <para><b>Only X11/XWayland windows are visible to it.</b> Under a Wayland session the compositor
/// exposes no focused-window title to clients at all, so a native Wayland window (most GTK4/Qt apps
/// there) reads as "" = Unknown and she simply says nothing about it. That is a platform limit, not a
/// bug: there is no portable Wayland API for this, by design.</para>
///
/// <para>Returns "" whenever there is no display, no active window or no title, never throws.
/// Called on the UI thread every 1.5s; one owned connection, opened lazily.</para>
/// </summary>
internal static class X11ActiveWindow
{
    private const string LibX11 = "libX11.so.6";
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport(LibX11, EntryPoint = "XSetErrorHandler")] private static extern IntPtr XRestoreErrorHandler(IntPtr previous);
    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
    [DllImport(LibX11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        long offset, long length, bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat,
        out ulong nItems, out ulong bytesAfter, out IntPtr prop);
    [DllImport(LibX11)] private static extern int XGetGeometry(IntPtr display, IntPtr window, out IntPtr root,
        out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(LibX11)] private static extern bool XTranslateCoordinates(IntPtr display, IntPtr source, IntPtr destination,
        int sourceX, int sourceY, out int destinationX, out int destinationY, out IntPtr child);
    [DllImport("libXrandr.so.2")] private static extern IntPtr XRRGetMonitors(IntPtr display, IntPtr window, bool active, out int count);
    [DllImport("libXrandr.so.2")] private static extern void XRRFreeMonitors(IntPtr monitors);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public IntPtr Name;
        public int Primary, Automatic, OutputCount, X, Y, Width, Height, MillimeterWidth, MillimeterHeight;
        public IntPtr Outputs;
    }

    // A window can close between the two reads; the default Xlib handler would exit the process on
    // that BadWindow. Xlib's handler is process-wide, so it is swapped in only around each read and
    // the previous one (Avalonia's, X11Overlay's, or Xlib's default) restored straight after. Each
    // read is a reply round trip, so its error is dispatched inside the window.
    // ponytail: another thread's X error landing inside that window is swallowed too; a per-display
    // handler needs XCB.
    private static readonly XErrorHandler IgnoreErrors = (_, _) => 0;

    private static IntPtr _display;
    private static bool _tried;
    private static IntPtr _root, _active, _netWmName, _utf8, _wmName, _netWmPid, _cardinal,
        _windowType, _typeDesktop, _typeDock, _frameExtents;

    /// <summary>Set once by the test assembly's module initializer: no test may read the real desktop.</summary>
    internal static bool Disabled;

    public static string ReadTitle() => ReadForeground().Title;

    // WPF AwarenessProbes.cs:72-90 reads title and process from the same foreground handle.
    // X11 exposes the client PID on that window; missing or stale PIDs leave the process unknown.
    internal static (string Title, string Process) ReadForeground() =>
        ReadActive(window => (ReadTitle(window), ReadProcessName(window)), ("", ""));

    /// <summary>Restack b179 (#3946): the v2 observer's foreground sample (WPF AwarenessProbes.cs:72-96,
    /// 135-149) from the same active window: title, normalised process and the fullscreen flag. A
    /// desktop or dock window is never fullscreen. Null when nothing can be read.</summary>
    internal static ForegroundSample? ReadSample() => ReadActive<ForegroundSample?>(window =>
    {
        var types = ReadCardinals(window, _windowType, 32, IntPtr.Zero);
        bool shell = Array.IndexOf(types, unchecked((uint)_typeDesktop.ToInt64())) >= 0
            || Array.IndexOf(types, unchecked((uint)_typeDock.ToInt64())) >= 0;
        return CreateSample(window, ReadTitle(window), ReadProcessName(window), shell, !shell && CoversMonitor(window));
    }, null);

    // One owned connection and the swapped-in error handler around every read of the active window.
    private static T ReadActive<T>(Func<IntPtr, T> read, T none)
    {
        if (Disabled) return none;
        try
        {
            if (!_tried)
            {
                _tried = true;
                if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return none;
                _display = XOpenDisplay(IntPtr.Zero);
                if (_display == IntPtr.Zero) return none;
                _root = XDefaultRootWindow(_display);
                _active = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);
                _netWmName = XInternAtom(_display, "_NET_WM_NAME", false);
                _utf8 = XInternAtom(_display, "UTF8_STRING", false);
                _wmName = XInternAtom(_display, "WM_NAME", false);
                _netWmPid = XInternAtom(_display, "_NET_WM_PID", false);
                _cardinal = XInternAtom(_display, "CARDINAL", false);
                _windowType = XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
                _typeDesktop = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DESKTOP", false);
                _typeDock = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DOCK", false);
                _frameExtents = XInternAtom(_display, "_NET_FRAME_EXTENTS", false);
            }
            if (_display == IntPtr.Zero) return none;

            var previous = XSetErrorHandler(IgnoreErrors);
            try
            {
                var window = ReadWindow(_root, _active);
                return window == IntPtr.Zero ? none : read(window);
            }
            finally { XRestoreErrorHandler(previous); }
        }
        catch { return none; }   // no libX11 on this machine, or the window went away: nothing to observe
    }

    private static string ReadTitle(IntPtr window) =>
        ReadString(window, _netWmName, _utf8) ?? ReadString(window, _wmName, IntPtr.Zero /* AnyPropertyType */) ?? "";

    internal static ForegroundSample CreateSample(IntPtr window, string title, string? process, bool shell, bool covers)
    {
        var lower = (process ?? "").ToLowerInvariant();
        return new(window, title, lower.EndsWith(".exe", StringComparison.Ordinal) ? lower[..^4] : lower, !shell && covers);
    }

    // Xlib stores format-32 items in native longs. A required type also requires exactly `length` items
    // (EWMH frame extents are four CARDINALs).
    private static uint[] ReadCardinals(IntPtr window, IntPtr property, int length, IntPtr requiredType)
    {
        if (XGetWindowProperty(_display, window, property, 0, length, false, requiredType,
                out var type, out var format, out var count, out var remaining, out var data) != 0 || data == IntPtr.Zero) return Array.Empty<uint>();
        try
        {
            if (format != 32 || (requiredType != IntPtr.Zero && (type != requiredType || count != (ulong)length || remaining != 0))) return Array.Empty<uint>();
            var values = new uint[(int)count];
            for (int i = 0; i < values.Length; i++) values[i] = unchecked((uint)Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64());
            return values;
        }
        finally { XFree(data); }
    }

    private static bool CoversMonitor(IntPtr window)
    {
        if (XGetGeometry(_display, window, out _, out _, out _, out var width, out var height, out _, out _) == 0 ||
            !XTranslateCoordinates(_display, window, _root, 0, 0, out var x, out var y, out _)) return false;
        IntPtr monitors = IntPtr.Zero;
        try
        {
            monitors = XRRGetMonitors(_display, _root, true, out int count);
            int size = Marshal.SizeOf<MonitorInfo>();
            var rectangles = new List<(int X, int Y, int Width, int Height)>();
            for (int i = 0; monitors != IntPtr.Zero && i < count; i++)
            {
                var m = Marshal.PtrToStructure<MonitorInfo>(IntPtr.Add(monitors, i * size));
                rectangles.Add((m.X, m.Y, m.Width, m.Height));
            }
            return CoversBestMonitor(x, y, width, height, rectangles, ReadCardinals(window, _frameExtents, 4, _cardinal));
        }
        catch { return false; }   // no RandR or no monitor: the optional fullscreen signal is absent
        finally { if (monitors != IntPtr.Zero) XRRFreeMonitors(monitors); }
    }

    // MonitorFromWindow picks the monitor with the greatest intersection (WPF AwarenessProbes.cs:139);
    // the frame extents count as part of the window first.
    internal static bool CoversBestMonitor(long x, long y, long width, long height,
        IEnumerable<(int X, int Y, int Width, int Height)> monitors, uint[]? extents = null)
    {
        if (extents?.Length == 4)
        {
            x -= extents[0]; y -= extents[2];
            width += (long)extents[0] + extents[1]; height += (long)extents[2] + extents[3];
        }
        long best = 0; bool covers = false;
        foreach (var m in monitors)
        {
            if (m.Width <= 0 || m.Height <= 0) continue;
            long overlapWidth = Math.Max(0, Math.Min(x + width, (long)m.X + m.Width) - Math.Max(x, m.X));
            long overlapHeight = Math.Max(0, Math.Min(y + height, (long)m.Y + m.Height) - Math.Max(y, m.Y));
            long area = overlapWidth * overlapHeight;
            if (area <= best) continue;
            best = area; covers = CoversMonitor(x, y, width, height, m.X, m.Y, m.Width, m.Height);
        }
        return covers;
    }

    // WPF AwarenessProbes.cs:59-60,135-149: two pixels of slack on every edge.
    internal static bool CoversMonitor(long x, long y, long width, long height, int mx, int my, int mw, int mh) =>
        mw > 0 && mh > 0 && x <= (long)mx + 2 && y <= (long)my + 2 &&
        x + width >= (long)mx + mw - 2 && y + height >= (long)my + mh - 2;

    private static string ReadProcessName(IntPtr window)
    {
        if (XGetWindowProperty(_display, window, _netWmPid, 0, 1, false, _cardinal,
                out var actualType, out var format, out var n, out _, out var prop) != 0 || prop == IntPtr.Zero) return "";
        try
        {
            if (actualType != _cardinal || format != 32 || n != 1) return "";
            var pid = Marshal.ReadIntPtr(prop).ToInt64(); // Xlib stores format-32 values as longs
            if (pid <= 0 || pid > int.MaxValue) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName ?? "";
        }
        catch { return ""; }
        finally { XFree(prop); }
    }

    private static IntPtr ReadWindow(IntPtr window, IntPtr property)
    {
        if (XGetWindowProperty(_display, window, property, 0, 1, false, IntPtr.Zero, out _, out var format,
                out var n, out _, out var prop) != 0 || prop == IntPtr.Zero) return IntPtr.Zero;
        try { return format == 32 && n > 0 ? Marshal.ReadIntPtr(prop) : IntPtr.Zero; }   // format-32 items are longs
        finally { XFree(prop); }
    }

    private static string? ReadString(IntPtr window, IntPtr property, IntPtr type)
    {
        if (XGetWindowProperty(_display, window, property, 0, 1024, false, type, out _, out var format,
                out var n, out _, out var prop) != 0 || prop == IntPtr.Zero) return null;
        try
        {
            if (format != 8 || n == 0) return null;
            var bytes = new byte[(int)n];
            Marshal.Copy(prop, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { XFree(prop); }
    }
}

// The Linux twin of Win32AwarenessForegroundProbe for the v2 observer seam (WPF AwarenessObserver.cs:160,173,603).
internal sealed class X11ForegroundProbe : IForegroundProbe
{
    public ForegroundSample? Read() => X11ActiveWindow.ReadSample();
}
