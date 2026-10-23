using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// A bare Win32 popup filled with one colour and blended by the desktop compositor at one
/// constant alpha (WS_EX_LAYERED + LWA_ALPHA). No WPF visual tree, no per-pixel surface: the
/// fill is painted once and every alpha step is a single call, whatever the size.
///
/// <para>Two users. The Pink Rush screen wash (ccp-bugs #1415): the old wash was a WPF
/// AllowsTransparency window the size of the whole virtual desktop, which is a per-pixel
/// layered surface re-uploaded on every animated frame. And the red bubble press catcher
/// (ccp-bugs #1406): a near-invisible patch that takes a click the mouse hook must let
/// through, so it does not land on the app behind the bubble.</para>
///
/// <para>Create, move and destroy on the UI thread (the window lives on its creator's message
/// loop). All coordinates are PHYSICAL pixels.</para>
/// </summary>
internal sealed class NativeLayeredWindow : IDisposable
{
    private const string ClassName = "CcpNativeLayeredWindow";
    private static readonly WndProcDelegate s_proc = StaticWndProc;   // kept alive for the class
    private static readonly Dictionary<IntPtr, NativeLayeredWindow> s_live = new();
    private static readonly object s_sync = new();
    private static bool s_registered;

    private IntPtr _brush;

    /// <summary>The window handle, or zero when creation failed or it was disposed.</summary>
    public IntPtr Handle { get; private set; }

    /// <summary>Optional message tap. Return a value to end handling, null to fall through.</summary>
    public Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? Message;

    /// <param name="r">Fill colour.</param><param name="g">Fill colour.</param><param name="b">Fill colour.</param>
    /// <param name="clickThrough">True: input falls through to whatever is below (a visual).
    /// False: the window takes the mouse (a catcher). It never activates either way.</param>
    public NativeLayeredWindow(byte r, byte g, byte b, bool clickThrough)
    {
        try
        {
            var hInstance = GetModuleHandle(null);
            lock (s_sync)
            {
                if (!s_registered)
                {
                    var wc = new WNDCLASSEX
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc),
                        hInstance = hInstance,
                        hCursor = LoadCursor(IntPtr.Zero, (IntPtr)IDC_ARROW),
                        lpszClassName = ClassName,
                    };
                    // A second registration (class already there) is fine: the name is ours.
                    RegisterClassEx(ref wc);
                    s_registered = true;
                }
            }

            _brush = CreateSolidBrush((uint)(r | (g << 8) | (b << 16)));
            int ex = WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                     | (clickThrough ? WS_EX_TRANSPARENT : 0);
            Handle = CreateWindowEx(ex, ClassName, string.Empty, WS_POPUP, 0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (Handle == IntPtr.Zero) { DeleteBrush(); return; }
            lock (s_sync) s_live[Handle] = this;
            SetAlpha(255);
        }
        catch
        {
            Handle = IntPtr.Zero;
            DeleteBrush();
        }
    }

    /// <summary>0..255 across the whole window.</summary>
    public void SetAlpha(byte alpha)
    {
        if (Handle != IntPtr.Zero) SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    /// <summary>Place it (physical px), put it at the top of the topmost band and show it,
    /// without activating. Safe from a mouse hook callback: one synchronous call.</summary>
    public void ShowAt(int x, int y, int w, int h)
    {
        if (Handle == IntPtr.Zero) return;
        SetWindowPos(Handle, HWND_TOPMOST, x, y, Math.Max(1, w), Math.Max(1, h),
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void Hide()
    {
        if (Handle != IntPtr.Zero) ShowWindow(Handle, SW_HIDE);
    }

    public void Dispose()
    {
        var h = Handle;
        Handle = IntPtr.Zero;
        if (h != IntPtr.Zero)
        {
            lock (s_sync) s_live.Remove(h);
            try { DestroyWindow(h); } catch { /* already gone */ }
        }
        DeleteBrush();
    }

    private void DeleteBrush()
    {
        if (_brush == IntPtr.Zero) return;
        try { DeleteObject(_brush); } catch { /* best effort */ }
        _brush = IntPtr.Zero;
    }

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            NativeLayeredWindow? self;
            lock (s_sync) s_live.TryGetValue(hwnd, out self);
            if (self != null)
            {
                if (msg == WM_ERASEBKGND && self._brush != IntPtr.Zero && GetClientRect(hwnd, out var rc))
                {
                    FillRect(wParam, ref rc, self._brush);
                    return (IntPtr)1;
                }
                if (msg == WM_MOUSEACTIVATE) return (IntPtr)MA_NOACTIVATE;
                var handled = self.Message?.Invoke(hwnd, msg, wParam, lParam);
                if (handled.HasValue) return handled.Value;
            }
        }
        catch { /* a window procedure must never throw into the message loop */ }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    #region Win32

    internal const uint WM_ERASEBKGND = 0x0014;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    internal const uint WM_TIMER = 0x0113;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint WM_RBUTTONUP = 0x0205;
    private const int MA_NOACTIVATE = 3;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST = 0x8;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private const int IDC_ARROW = 32512;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rc, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint colorRef);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    #endregion
}
