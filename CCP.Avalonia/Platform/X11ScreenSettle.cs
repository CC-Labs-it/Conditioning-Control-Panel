using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Before Avalonia connects: waits until the X server reports its real monitors. A cold WSLg start (WSL had
/// stopped) and an on-demand Xwayland (GNOME starts it with the first X client) first report Xwayland's startup
/// default, one 640 x 480 monitor at 0,0, for under a second (0.86 s measured on the owner's PC, 2026-11-07:
/// "rdp-0 0,0 640x480", then rdp-0 1680,0 2560x1440 + rdp-2 0,235 1680x1050). Avalonia 12 cached that first
/// answer for the whole run: the splash was centred on a 640 x 480 box in a corner, the main window fitted to
/// 640 x 480, and everything sized from Screens would be too. On a warm server this is one round trip.
/// </summary>
internal static class X11ScreenSettle
{
    /// <summary>How long a placeholder is waited out before the app starts anyway.</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    /// <summary>Xwayland's startup default: exactly one 640 x 480 monitor at the origin.</summary>
    internal static bool IsPlaceholder(IReadOnlyList<PixelRect> monitors) =>
        monitors.Count == 1 && monitors[0] == new PixelRect(0, 0, 640, 480);

    internal static void WaitForRealMonitors()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return;
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;   // Avalonia reports a missing server itself
            try
            {
                // RandR 1.5 or nothing: asking a server without it would hit Xlib's default error handler (exit).
                if (!XRRQueryExtension(display, out _, out _) || XRRQueryVersion(display, out var major, out var minor) == 0
                    || major < 1 || (major == 1 && minor < 5)) return;
                var root = XDefaultRootWindow(display);
                var clock = Stopwatch.StartNew();
                var waited = false;
                while (true)
                {
                    var monitors = Read(display, root);
                    if (!IsPlaceholder(monitors))
                    {
                        if (waited)
                            Serilog.Log.Information("[Placement] X server first reported its 640x480 placeholder screen; real monitors after {Ms} ms", clock.ElapsedMilliseconds);
                        return;
                    }
                    if (clock.Elapsed >= Budget)
                    {
                        Serilog.Log.Warning("[Placement] X server still reports only a 640x480 screen after {Ms} ms; starting anyway", clock.ElapsedMilliseconds);
                        return;
                    }
                    waited = true;
                    Thread.Sleep(40);
                }
            }
            finally { XCloseDisplay(display); }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[Placement] X monitor settle skipped"); }
    }

    private static List<PixelRect> Read(IntPtr display, IntPtr root)
    {
        var list = new List<PixelRect>();
        var monitors = XRRGetMonitors(display, root, true, out var count);
        if (monitors == IntPtr.Zero) return list;
        try
        {
            var size = Marshal.SizeOf<XRRMonitorInfo>();
            for (var i = 0; i < count; i++)
            {
                var m = Marshal.PtrToStructure<XRRMonitorInfo>(monitors + i * size);
                list.Add(new PixelRect(m.X, m.Y, m.Width, m.Height));
            }
        }
        finally { XRRFreeMonitors(monitors); }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRRMonitorInfo
    {
        public IntPtr Name;
        public int Primary, Automatic, NOutput;
        public int X, Y, Width, Height;
        public int MWidth, MHeight;
        public IntPtr Outputs;
    }

    private const string LibX11 = "libX11.so.6";
    private const string LibXrandr = "libXrandr.so.2";
    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibXrandr)] private static extern bool XRRQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(LibXrandr)] private static extern int XRRQueryVersion(IntPtr display, out int major, out int minor);
    [DllImport(LibXrandr)] private static extern IntPtr XRRGetMonitors(IntPtr display, IntPtr window, bool getActive, out int count);
    [DllImport(LibXrandr)] private static extern void XRRFreeMonitors(IntPtr monitors);
}
