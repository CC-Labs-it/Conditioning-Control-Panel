using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

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

    internal static Task<int?> ReadSecondsAsync()
    {
        if (OperatingSystem.IsLinux() && (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))))
            return ReadWaylandAsync(ReadMutterMillisecondsAsync);
        return Task.FromResult(ReadSeconds());
    }

    // Mutter's idle monitor measures desktop-wide input and returns milliseconds. A missing or
    // unresponsive session bus is an unavailable probe, bounded independently of the UI thread.
    internal static async Task<int?> ReadWaylandAsync(Func<Task<ulong>> readMilliseconds)
    {
        try { return (int)Math.Min((await readMilliseconds().WaitAsync(TimeSpan.FromSeconds(2))) / 1000, int.MaxValue); }
        catch { return null; }
    }

    private static async Task<ulong> ReadMutterMillisecondsAsync()
    {
        using var bus = new DBusConnection(DBusAddress.Session!);
        // Disposing this connection on timeout also ends any outstanding call.
        async Task<ulong> ReadAsync()
        {
            await bus.ConnectAsync();
            var writer = bus.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.gnome.Mutter.IdleMonitor",
                path: "/org/gnome/Mutter/IdleMonitor/Core", @interface: "org.gnome.Mutter.IdleMonitor", member: "GetIdletime");
            return await bus.CallMethodAsync(writer.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadUInt64(), null);
        }
        return await ReadAsync().WaitAsync(TimeSpan.FromSeconds(2));
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
