using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Right-button interception for the ripple. X11 replays misses to the original target;
/// Windows uses the same low-level mouse hook as WPF. Owned only during a run.</summary>
internal sealed class RipplePointer : IDisposable
{
    private readonly Func<PixelPoint, bool> _down;
    private volatile bool _stopped;
    private Thread? _thread;
    private IntPtr _hook;
    private readonly HookProc _proc;

    private RipplePointer(Func<PixelPoint, bool> down) { _down = down; _proc = OnMouse; }

    /// <summary>Tests set this so no run ever grabs the real desktop's right button.</summary>
    internal static bool Disabled;

    internal static RipplePointer? Start(Func<PixelPoint, bool> down)
    {
        if (Disabled) return null;
        var input = new RipplePointer(down);
        if (OperatingSystem.IsWindows())
        {
            input._hook = SetWindowsHookEx(14, input._proc, GetModuleHandle(null), 0);
            if (input._hook != IntPtr.Zero) return input;
            Log.Warning("Ripple: mouse hook could not be installed");
            return null;
        }
        // Initialising the existing X11 seam also installs its process-wide X error handler.
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8 || !X11Overlay.IsAvailable) return null;
        input._thread = new Thread(input.Listen) { IsBackground = true, Name = "chaos-ripple-x11" };
        input._thread.Start();
        return input;
    }

    private void Listen()
    {
        IntPtr display = IntPtr.Zero, ev = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero || _stopped) return;
            var root = XDefaultRootWindow(display);
            // Individual modifiers allow unconflicted combinations even if a window manager has
            // already grabbed one. Sync mode permits ReplayPointer on every miss.
            X11Overlay.Synchronize(() =>
            {
                for (uint mods = 0; mods < 256; mods++)
                    XGrabButton(display, 3, mods, root, false, 4 | 8, 0, 1, IntPtr.Zero, IntPtr.Zero);
                XSync(display, false);
            });
            ev = Marshal.AllocHGlobal(192); // XEvent on 64-bit X11
            while (!_stopped)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, ev);
                    if (Marshal.ReadInt32(ev) != 4) continue; // ButtonPress
                    bool consume = false;
                    try
                    {
                        if (!_stopped) consume = _down(new PixelPoint(Marshal.ReadInt32(ev, 72), Marshal.ReadInt32(ev, 76)));
                    }
                    catch (Exception ex) { Log.Debug(ex, "Ripple: input callback failed"); }
                    XAllowEvents(display, consume ? 0 : 2, IntPtr.Zero); // AsyncPointer / ReplayPointer
                    if (consume) XUngrabPointer(display, IntPtr.Zero); // swallow down, let the later up through
                    XFlush(display);
                }
                Thread.Sleep(10);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Ripple: X11 listener stopped"); }
        finally
        {
            if (display != IntPtr.Zero)
            {
                XAllowEvents(display, 0, IntPtr.Zero);
                XUngrabButton(display, 3, 1u << 15, XDefaultRootWindow(display));
                XCloseDisplay(display);
            }
            if (ev != IntPtr.Zero) Marshal.FreeHGlobal(ev);
        }
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (!_stopped && code >= 0 && (int)message == 0x0204 &&
                _down(new PixelPoint(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)))) return (IntPtr)1;
        }
        catch (Exception ex) { Log.Debug(ex, "Ripple: mouse hook callback failed"); }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        _stopped = true;
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _thread?.Join(250);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XGrabButton(IntPtr display, uint button, uint modifiers, IntPtr window, bool owner, uint mask, int pointerMode, int keyboardMode, IntPtr confine, IntPtr cursor);
    [DllImport("libX11.so.6")] private static extern int XUngrabButton(IntPtr display, uint button, uint modifiers, IntPtr window);
    [DllImport("libX11.so.6")] private static extern int XAllowEvents(IntPtr display, int mode, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XUngrabPointer(IntPtr display, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
}
