using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Services.Awareness;
using System.Text;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Linux title source for <c>WindowAwarenessService</c> (WPF: user32 GetForegroundWindow +
/// GetWindowText, App.xaml.cs ReadForegroundWindowTitle). Reads the EWMH root property
/// <c>_NET_ACTIVE_WINDOW</c>, then that window's <c>_NET_WM_NAME</c> (UTF-8), falling back to
/// <c>WM_NAME</c>.
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
    [DllImport(LibX11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        long offset, long length, bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat,
        out ulong nItems, out ulong bytesAfter, out IntPtr prop);

    // A window can close between the two reads; the default Xlib handler would exit the process on
    // that BadWindow. Xlib's handler is process-wide, so it is swapped in only around each read and
    // the previous one (Avalonia's, X11Overlay's, or Xlib's default) restored straight after. Each
    // read is a reply round trip, so its error is dispatched inside the window.
    // ponytail: another thread's X error landing inside that window is swallowed too; a per-display
    // handler needs XCB.
    private static readonly XErrorHandler IgnoreErrors = (_, _) => 0;

    private static IntPtr _display;
    private static bool _tried;
    private static IntPtr _root, _active, _netWmName, _utf8, _wmName, _pid, _windowType, _desktop, _dock, _frameExtents;

    /// <summary>Set once by the test assembly's module initializer: no test may read the real desktop.</summary>
    internal static bool Disabled;

    public static string ReadTitle() => ReadActive(window => ReadTitle(window)) ?? "";

    // WPF AwarenessProbes.cs:72-96. One active X11 window supplies title, process and geometry.
    internal static ForegroundSample? ReadForeground() => ReadActive(window =>
    {
        var types = ReadValues(window, _windowType, 32);
        bool shell = types.Contains((uint)_desktop.ToInt64()) || types.Contains((uint)_dock.ToInt64());
        return CreateSample(window, ReadTitle(window), ProcessName(ReadValues(window, _pid, 1).FirstOrDefault()), shell,
            !shell && CoversMonitor(window));
    });
    private static T? ReadActive<T>(Func<IntPtr, T> read) where T : class
    {
        if (Disabled) return null;
        try
        {
            if (!_tried)
            {
                _tried = true;
                if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return null;
                _display = XOpenDisplay(IntPtr.Zero);
                if (_display == IntPtr.Zero) return null;
                _root = XDefaultRootWindow(_display);
                _active = XInternAtom(_display, "_NET_ACTIVE_WINDOW", false);
                _netWmName = XInternAtom(_display, "_NET_WM_NAME", false);
                _utf8 = XInternAtom(_display, "UTF8_STRING", false);
                _wmName = XInternAtom(_display, "WM_NAME", false);
                _pid = XInternAtom(_display, "_NET_WM_PID", false);
                _windowType = XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
                _desktop = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DESKTOP", false);
                _dock = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DOCK", false);
                _frameExtents = XInternAtom(_display, "_NET_FRAME_EXTENTS", false);
            }
            if (_display == IntPtr.Zero) return null;

            var previous = XSetErrorHandler(IgnoreErrors);
            try
            {
                var window = ReadWindow(_root, _active);
                return window == IntPtr.Zero ? null : read(window);
            }
            finally { XRestoreErrorHandler(previous); }
        }
        catch { return null; }   // unavailable X11 or a window that disappeared
    }

    private static string ReadTitle(IntPtr window) => ReadString(window, _netWmName, _utf8) ?? ReadString(window, _wmName, IntPtr.Zero) ?? "";

    // Xlib stores format-32 items in native longs. EWMH frame extents require four CARDINALs.
    private static uint[] ReadValues(IntPtr window, IntPtr property, int length, IntPtr requiredType = default)
    {
        if (XGetWindowProperty(_display, window, property, 0, length, false, requiredType,
                out var type, out var format, out var count, out var remaining, out var data) != 0 || data == IntPtr.Zero) return Array.Empty<uint>();
        try {
            if (format != 32 || (requiredType != IntPtr.Zero && (type != requiredType || count != (ulong)length || remaining != 0))) return Array.Empty<uint>();
            var values = new uint[(int)count];
            for (int i = 0; i < values.Length; i++) values[i] = unchecked((uint)Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64());
            return values;
        } finally { XFree(data); }
    }
    private static string ProcessName(uint pid)
    {
        try {
            if (pid == 0 || pid > int.MaxValue) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        } catch { return ""; } // exited or inaccessible, as in WPF AwarenessProbes.cs:109-132
    }
    internal static ForegroundSample CreateSample(IntPtr window, string title, string? process, bool shell, bool covers)
    {
        var lower = (process ?? "").ToLowerInvariant();
        return new(window, title, lower.EndsWith(".exe", StringComparison.Ordinal) ? lower[..^4] : lower, !shell && covers);
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
            for (int i = 0; monitors != IntPtr.Zero && i < count; i++) {
                var m = Marshal.PtrToStructure<MonitorInfo>(IntPtr.Add(monitors, i * size));
                rectangles.Add((m.X, m.Y, m.Width, m.Height));
            }
            return CoversBestMonitor(x, y, width, height, rectangles, ReadValues(window, _frameExtents, 4, new IntPtr(6)));
        }
        catch { } // no RandR or no monitor: the optional fullscreen signal is absent
        finally { if (monitors != IntPtr.Zero) XRRFreeMonitors(monitors); }
        return false;
    }
    // MonitorFromWindow selects the monitor with the greatest intersection (WPF AwarenessProbes.cs:139).
    internal static bool CoversBestMonitor(long x, long y, long width, long height,
        IEnumerable<(int X, int Y, int Width, int Height)> monitors, uint[]? extents = null)
    {
        if (extents?.Length == 4) {
            x -= extents[0]; y -= extents[2];
            width += (long)extents[0] + extents[1]; height += (long)extents[2] + extents[3];
        }
        long best = 0; bool covers = false;
        foreach (var m in monitors)
        {
            if (m.Width <= 0 || m.Height <= 0) continue;
            long overlapWidth = Math.Max(0, Math.Min((long)x + width, (long)m.X + m.Width) - Math.Max((long)x, m.X));
            long overlapHeight = Math.Max(0, Math.Min((long)y + height, (long)m.Y + m.Height) - Math.Max((long)y, m.Y));
            long area = overlapWidth * overlapHeight;
            if (area <= best) continue;
            best = area; covers = CoversMonitor(x, y, width, height, m.X, m.Y, m.Width, m.Height);
        }
        return covers;
    }
    // WPF AwarenessProbes.cs:59-60,135-149: two pixels of slack on every edge.
    internal static bool CoversMonitor(long x, long y, long width, long height, int mx, int my, int mw, int mh) =>
        mw > 0 && mh > 0 && x <= (long)mx + 2 && y <= (long)my + 2 &&
        (long)x + width >= (long)mx + mw - 2 && (long)y + height >= (long)my + mh - 2;

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

// Existing IForegroundProbe seam used by WPF AwarenessObserver.cs:160,173,603.
internal sealed class X11ForegroundProbe : IForegroundProbe
{
    public ForegroundSample? Read() => X11ActiveWindow.ReadForeground();
}
