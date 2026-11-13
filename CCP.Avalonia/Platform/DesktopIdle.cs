using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        {
            WaylandIdleNotify.Shared.Start();
            return WaylandIdleNotify.Shared.Live ? Task.FromResult<int?>(WaylandIdleNotify.Shared.Seconds)
                : ReadWaylandAsync(ReadMutterMillisecondsAsync);
        }
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

// KDE and wlroots: ext-idle-notify-v1 reports when the seat has been idle for five minutes and
// when input resumes, so idle time is derived from the idled edge. GNOME keeps Mutter's probe.
internal sealed class WaylandIdleNotify
{
    internal static readonly WaylandIdleNotify Shared = new();
    internal const uint TimeoutMs = 300_000;
    internal Func<long> Now = () => Environment.TickCount64;
    private long _idledAt = -1;
    private int _started;
    internal volatile bool Live;

    internal void OnEvent(uint opcode) => Interlocked.Exchange(ref _idledAt, opcode == 0 ? Now() : -1);
    internal int Seconds => Interlocked.Read(ref _idledAt) is var at && at < 0 ? 0 : (int)((Now() - at + TimeoutMs) / 1000);

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) new Thread(() =>
        {
            try
            {
                var name = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "wayland-0";
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/", name)));
                using var stream = new NetworkStream(socket);
                Run(stream, stream);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] Wayland idle notify unavailable"); }
            finally { Live = false; }
        }) { IsBackground = true, Name = "wayland-idle" }.Start();
    }

    // Minimal wire client: display 1, registry 2, sync 3, seat 4, notifier 5, notification 6.
    internal void Run(Stream rx, Stream tx)
    {
        tx.Write(Message(1, 1, 2u).Concat(Message(1, 0, 3u)).ToArray());
        uint seat = 0, notifier = 0, version = 0, obj, op;
        byte[] body;
        while (Read(rx, out obj, out op, out body) && !(obj == 3 && op == 0))
        {
            if (obj != 2 || op != 0) continue;
            int len = BitConverter.ToInt32(body, 4);
            var iface = Encoding.ASCII.GetString(body, 8, Math.Max(0, len - 1));
            if (iface == "wl_seat" && seat == 0) seat = BitConverter.ToUInt32(body, 0);
            else if (iface == "ext_idle_notifier_v1")
                (notifier, version) = (BitConverter.ToUInt32(body, 0), BitConverter.ToUInt32(body, 8 + ((len + 3) & ~3)));
        }
        if (seat == 0 || notifier == 0) return;
        // v2's input notification ignores idle inhibitors, as WPF's GetLastInputInfo does.
        tx.Write(Message(2, 0, seat, "wl_seat", 1u, 4u).Concat(Message(2, 0, notifier, "ext_idle_notifier_v1", Math.Min(version, 2), 5u))
            .Concat(Message(5, version >= 2 ? 2u : 1u, 6u, TimeoutMs, 4u)).ToArray());
        Live = true;
        while (Read(rx, out obj, out op, out _)) if (obj == 6) OnEvent(op);
    }

    // Arguments are uint or string; strings carry length with NUL and pad to four bytes.
    internal static byte[] Message(uint obj, uint op, params object[] args)
    {
        var body = new List<byte>();
        foreach (var a in args)
            if (a is string t) body.AddRange(BitConverter.GetBytes((uint)t.Length + 1).Concat(Encoding.ASCII.GetBytes(t)).Concat(new byte[4 - t.Length % 4]));
            else body.AddRange(BitConverter.GetBytes((uint)a));
        return BitConverter.GetBytes(obj).Concat(BitConverter.GetBytes((uint)(body.Count + 8) << 16 | op)).Concat(body).ToArray();
    }

    private static bool Read(Stream rx, out uint obj, out uint op, out byte[] body)
    {
        var head = new byte[8];
        (obj, op, body) = (0, 0, Array.Empty<byte>());
        if (rx.ReadAtLeast(head, 8, false) < 8 || BitConverter.ToUInt32(head, 4) >> 16 is var size && size < 8) return false;
        (obj, op, body) = (BitConverter.ToUInt32(head, 0), BitConverter.ToUInt32(head, 4) & 0xffff, new byte[size - 8]);
        return rx.ReadAtLeast(body, body.Length, false) == body.Length;
    }
}
