using System;
using System.Runtime.InteropServices;
using System.Threading;
namespace ConditioningControlPanel.Avalonia.Platform;
// XInput2 raw left presses, on a private connection. No grab, no stored positions.
internal sealed class X11GazeDriftMouseHook : IDisposable
{
    private const string X11 = "libX11.so.6", Xi = "libXi.so.6";
    [StructLayout(LayoutKind.Sequential)] private struct Mask { public int Device, Length; public IntPtr Bits; }
    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] private static extern bool XQueryExtension(IntPtr display, string name, out int opcode, out int ev, out int err);
    [DllImport(X11)] private static extern int XPending(IntPtr display);
    [DllImport(X11)] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(X11)] private static extern bool XGetEventData(IntPtr display, IntPtr ev);
    [DllImport(X11)] private static extern void XFreeEventData(IntPtr display, IntPtr ev);
    [DllImport(X11)] private static extern int XFlush(IntPtr display);
    [DllImport(X11)] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int x, out int y, out int wx, out int wy, out uint buttons);
    [DllImport(Xi)] private static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);
    [DllImport(Xi)] private static extern int XISelectEvents(IntPtr display, IntPtr window, ref Mask mask, int count);
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _thread; private int _disposed;
    private X11GazeDriftMouseHook(IntPtr display, int opcode, Action<int, int> click)
    {
        _thread = new Thread(() => Listen(display, opcode, click)) { IsBackground = true, Name = "gaze-click-x11" };
        _thread.Start();
    }
    private static IntPtr Open(out int opcode)
    {
        opcode = 0;
        if (IntPtr.Size != 8 || !OperatingSystem.IsLinux() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            int major = 2, minor = 2;
            if (display != IntPtr.Zero && XQueryExtension(display, "XInputExtension", out opcode, out _, out _) && XIQueryVersion(display, ref major, ref minor) == 0 && (major > 2 || major == 2 && minor >= 2)) return display;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        if (display != IntPtr.Zero) XCloseDisplay(display);
        return IntPtr.Zero;
    }
    internal static bool Available()
    {
        var display = Open(out _);
        if (display == IntPtr.Zero) return false;
        XCloseDisplay(display); return true;
    }
    internal static IDisposable? Start(Action<int, int> click)
    {
        var display = Open(out int opcode);
        if (display == IntPtr.Zero) return null;
        var bits = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(bits, 1 << 15); // XI_RawButtonPress, XIAllMasterDevices
            var mask = new Mask { Device = 1, Length = 4, Bits = bits };
            if (XISelectEvents(display, XDefaultRootWindow(display), ref mask, 1) != 0) { XCloseDisplay(display); return null; }
            XFlush(display);
            return new X11GazeDriftMouseHook(display, opcode, click);
        }
        finally { Marshal.FreeHGlobal(bits); }
    }
    private void Listen(IntPtr display, int opcode, Action<int, int> click)
    {
        var ev = Marshal.AllocHGlobal(192); // same XEvent/cookie offsets as X11PanicKey
        try
        {
            var root = XDefaultRootWindow(display);
            while (!_stop.IsSet)
            {
                if (XPending(display) == 0) { _stop.Wait(10); continue; }
                XNextEvent(display, ev);
                if (Marshal.ReadInt32(ev) != 35 || Marshal.ReadInt32(ev, 32) != opcode || Marshal.ReadInt32(ev, 36) != 15 || !XGetEventData(display, ev)) continue;
                try
                {
                    var data = Marshal.ReadIntPtr(ev, 48);
                    if (data != IntPtr.Zero && Marshal.ReadInt32(data, 56) == 1 && XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out _) && !_stop.IsSet) { try { click(x, y); } catch { } }
                }
                finally { XFreeEventData(display, ev); }
            }
        }
        finally { Marshal.FreeHGlobal(ev); XCloseDisplay(display); }
    }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _stop.Set(); if (_thread.Join(2000)) _stop.Dispose(); }
}
