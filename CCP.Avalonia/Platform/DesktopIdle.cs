using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Platform;

// Desktop-wide idle time, without collecting keys or pointer positions.
internal static class DesktopIdle
{
    private static readonly Win32AwarenessInputProbe WindowsProbe = new();

    internal static int? ReadSeconds()
    {
        if (OperatingSystem.IsWindows()) return WindowsProbe.IdleSeconds;
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return null;
        return ReadX11(null);
    }

    // XScreenSaver reports the time since the last input across the X11 desktop. Each read owns
    // its connection and info allocation; neither the X server nor the extension is guaranteed.
    internal static int? ReadX11(string? displayName)
    {
        IntPtr display = IntPtr.Zero, info = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(displayName);
            if (display == IntPtr.Zero || XScreenSaverQueryExtension(display, out _, out _) == 0) return null;
            info = XScreenSaverAllocInfo();
            if (info == IntPtr.Zero || XScreenSaverQueryInfo(display, XDefaultRootWindow(display), info) == 0) return null;
            var ms = Marshal.PtrToStructure<ScreenSaverInfo>(info).Idle.ToUInt64();
            return (int)Math.Min(ms / 1000, int.MaxValue);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return null; }
        finally
        {
            if (info != IntPtr.Zero) XFree(info);
            if (display != IntPtr.Zero) XCloseDisplay(display);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenSaverInfo
    {
        public UIntPtr Window;
        public int State, Kind;
        public UIntPtr TilOrSince, Idle, EventMask;
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern UIntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
    [DllImport("libXss.so.1")] private static extern int XScreenSaverQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport("libXss.so.1")] private static extern IntPtr XScreenSaverAllocInfo();
    [DllImport("libXss.so.1")] private static extern int XScreenSaverQueryInfo(IntPtr display, UIntPtr window, IntPtr info);
}
