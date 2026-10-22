using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The X11 / XWayland twin of <see cref="Win32OptionsDismiss"/>: WPF EmiOptionsWindow's
/// non-consuming click-away and Escape hooks (GlobalMouseHook LeftDown/RightDown, GlobalKeyboardHook).
///
/// <para><b>Events, not a state poll.</b> <c>XI_RawButtonPress</c> and <c>XI_RawKeyPress</c> selected
/// on the root window are delivered to us AND to the client under the pointer / in focus, like the
/// WPF hooks that returned false; a grab would swallow the click. A press released between two
/// reads is still an event, which the old 30 ms <see cref="X11Pointer"/> poll could miss.</para>
///
/// <para><b>Wayland-native windows.</b> XWayland never sees input aimed at a native Wayland window,
/// so a click there produces no raw event. What the compositor DOES publish is focus: when a native
/// Wayland surface (or the Wayland desktop) takes focus, the root's <c>_NET_ACTIVE_WINDOW</c> drops
/// to None. That transition folds the panel. Our own windows are X windows, so activating one never
/// produces None; clicks on other X windows are already covered by the raw button event.</para>
///
/// <para>Owned only while the panel is open: its own display connection and thread, closed on
/// Dispose. No key or pointer history is kept; only the Escape keycode and buttons 1/3 are compared.</para>
/// </summary>
internal sealed class X11OptionsDismiss : IDisposable
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXi = "libXi.so.6";
    // Values and offsets as in X11PanicKey (XInput2.h / Xlib.h on x86_64).
    private const int GenericEvent = 35, PropertyNotify = 28, XI_RawKeyPress = 13, XI_RawButtonPress = 15, XIAllMasterDevices = 1;
    private const int XEventSize = 192, CookieExtension = 32, CookieEvType = 36, CookieData = 48, RawDetail = 56, PropertyAtom = 40;
    private const long PropertyChangeMask = 1L << 22, XK_Escape = 0xff1b, XA_WINDOW = 33;

    [StructLayout(LayoutKind.Sequential)]
    private struct XIEventMask { public int DeviceId; public int MaskLen; public IntPtr Mask; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short Revents; }

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern bool XQueryExtension(IntPtr display, string name, out int opcode, out int evBase, out int errBase);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(LibX11)] private static extern int XPending(IntPtr display);
    [DllImport(LibX11)] private static extern int XConnectionNumber(IntPtr display);
    [DllImport(LibX11)] private static extern bool XGetEventData(IntPtr display, IntPtr cookie);
    [DllImport(LibX11)] private static extern void XFreeEventData(IntPtr display, IntPtr cookie);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr mask);
    [DllImport(LibX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport(LibX11)] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root,
        out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport(LibX11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        IntPtr offset, IntPtr length, bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat,
        out IntPtr items, out IntPtr bytesAfter, out IntPtr data);
    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
    [DllImport(LibXi)] private static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);
    [DllImport(LibXi)] private static extern int XISelectEvents(IntPtr display, IntPtr window, ref XIEventMask mask, int count);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref PollFd fds, uint count, int timeoutMs);

    private readonly Action<PixelPoint?, bool> _onPress;
    private readonly IntPtr _display, _root, _activeAtom;
    private readonly int _opcode;
    private readonly byte _esc;
    private volatile bool _disposed;

    private X11OptionsDismiss(Action<PixelPoint?, bool> onPress, IntPtr display, int opcode)
    {
        _onPress = onPress;
        _display = display;
        _opcode = opcode;
        _root = XDefaultRootWindow(display);
        _activeAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        _esc = XKeysymToKeycode(display, (IntPtr)XK_Escape);
    }

    /// <summary>Null when there is no X display or no XInput 2.2; the panel then keeps the X11Pointer fallback.</summary>
    internal static IDisposable? Start(Action<PixelPoint?, bool> onPress)
    {
        if (!OperatingSystem.IsLinux()) return null;
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            int major = 2, minor = 2;
            if (display == IntPtr.Zero || !XQueryExtension(display, "XInputExtension", out int opcode, out _, out _)
                || XIQueryVersion(display, ref major, ref minor) != 0)
            {
                if (display != IntPtr.Zero) XCloseDisplay(display);
                return null;
            }
            var listener = new X11OptionsDismiss(onPress, display, opcode);
            var bits = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(bits, RawMask);
                var mask = new XIEventMask { DeviceId = XIAllMasterDevices, MaskLen = 4, Mask = bits };
                XISelectEvents(display, listener._root, ref mask, 1);
            }
            finally { Marshal.FreeHGlobal(bits); }
            XSelectInput(display, listener._root, (IntPtr)PropertyChangeMask);
            XFlush(display);
            var thread = new Thread(listener.Listen) { IsBackground = true, Name = "EmiOptionsDismiss" };
            thread.Start();
            return listener;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] options X11 dismissal listener unavailable");
            if (display != IntPtr.Zero) XCloseDisplay(display);
            return null;
        }
    }

    /// <summary>The XI2 mask: raw key and raw button presses only (bits 13 and 15).</summary>
    internal const int RawMask = (1 << XI_RawKeyPress) | (1 << XI_RawButtonPress);

    /// <summary>WPF folds on LeftDown/RightDown only: X buttons 1 and 3, never wheel or middle.</summary>
    internal static bool IsDismissButton(int button) => button is 1 or 3;

    /// <summary>Focus left the X world (a native Wayland window or the desktop took it): fold.</summary>
    internal static bool FocusLeftX(IntPtr previous, IntPtr now) => now == IntPtr.Zero && previous != IntPtr.Zero;

    private void Listen()
    {
        var ev = Marshal.AllocHGlobal(XEventSize);
        var fd = new PollFd { Fd = XConnectionNumber(_display), Events = 1 /* POLLIN */ };
        IntPtr active = ReadActive();
        try
        {
            while (!_disposed)
            {
                try
                {
                    if (XPending(_display) == 0)
                    {
                        // Wait for the next event; the timeout only bounds how long Dispose takes.
                        fd.Revents = 0;
                        poll(ref fd, 1, 100);
                        continue;
                    }
                    XNextEvent(_display, ev);
                    int type = Marshal.ReadInt32(ev);
                    if (type == PropertyNotify)
                    {
                        if (Marshal.ReadIntPtr(ev, PropertyAtom) != _activeAtom) continue;
                        var now = ReadActive();
                        bool left = FocusLeftX(active, now);
                        active = now;
                        if (left && !_disposed) _onPress(null, true);
                        continue;
                    }
                    if (type != GenericEvent || Marshal.ReadInt32(ev, CookieExtension) != _opcode) continue;
                    if (!XGetEventData(_display, ev)) continue;
                    int evType = Marshal.ReadInt32(ev, CookieEvType);
                    int detail = Marshal.ReadInt32(Marshal.ReadIntPtr(ev, CookieData), RawDetail);
                    XFreeEventData(_display, ev);
                    if (_disposed) break;
                    if (evType == XI_RawKeyPress && _esc != 0 && detail == _esc)
                        _onPress(null, true);
                    else if (evType == XI_RawButtonPress && IsDismissButton(detail)
                             && XQueryPointer(_display, _root, out _, out _, out int x, out int y, out _, out _, out _))
                        _onPress(new PixelPoint(x, y), false);
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options X11 dismissal event failed"); }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ev);
            XCloseDisplay(_display);
        }
    }

    private IntPtr ReadActive()
    {
        if (_activeAtom == IntPtr.Zero) return IntPtr.Zero;
        if (XGetWindowProperty(_display, _root, _activeAtom, IntPtr.Zero, (IntPtr)1, false, (IntPtr)XA_WINDOW,
                out _, out int format, out IntPtr items, out _, out IntPtr data) != 0 || data == IntPtr.Zero)
            return IntPtr.Zero;
        try { return format == 32 && items != IntPtr.Zero ? Marshal.ReadIntPtr(data) : IntPtr.Zero; }
        finally { XFree(data); }
    }

    public void Dispose() => _disposed = true;
}
