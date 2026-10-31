using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Windows half of <see cref="X11Overlay"/>, reached only through its entry points when the
/// window's platform handle is an "HWND" - callers never see this class. Same bits the WPF head
/// sets on every desktop overlay (ConditioningControlPanel/Services/Notifications/OverlayService.cs:1415,
/// Services/Flash/FlashService.cs:4614 ApplyClickability, :4838 ForceTopmost):
/// WS_EX_LAYERED|WS_EX_TOOLWINDOW always, WS_EX_TRANSPARENT|WS_EX_NOACTIVATE normally toggled together with
/// click-through (as Chaos/ChaosOverlayWindow.xaml.cs:1073-1076 does, so an interactive mode can
/// take focus), HWND_TOPMOST via SetWindowPos.
///
/// <para><b>Why a style callback and not just SetWindowLong.</b> Avalonia 12.1.2's Win32
/// <c>WindowImpl.UpdateWindowProperties</c> rebuilds GWL_EXSTYLE from scratch (EDGE | NOREDIRECTIONBITMAP
/// | APPWINDOW) whenever ShowInTaskbar, decorations, resizability or window state change, which would
/// silently drop our bits. <see cref="Win32Properties.AddWindowStylesCallback"/> is its sanctioned hook
/// into that rebuild, so the bits survive it.</para>
///
/// <para><b>Opacity is the layered alpha</b> (SetLayeredWindowAttributes/LWA_ALPHA), the twin of
/// X11's _NET_WM_WINDOW_OPACITY: DWM applies it, the app never re-renders. Avalonia never sets
/// WS_EX_LAYERED itself (it uses DirectComposition + NOREDIRECTIONBITMAP), so nothing competes for it.
/// A freshly layered window stays invisible until its attributes are set once, hence the 255 below.</para>
/// </summary>
internal static class Win32Overlay
{
    private const int GwlExStyle = -20;
    internal const uint WsExTopmost = 0x8, WsExTransparent = 0x20, WsExToolWindow = 0x80,
        WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
    internal const uint OverlayBits = WsExLayered | WsExToolWindow;
    private const uint ClickThroughBits = WsExTransparent | WsExNoActivate;
    private const uint LwaAlpha = 2;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern uint GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern uint SetWindowLong(IntPtr hwnd, int index, uint value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    internal static bool RestackAbove(IntPtr hwnd, IntPtr sibling)
    {
        if (!OperatingSystem.IsWindows() || !IsWindow(hwnd) || !IsWindow(sibling)) return false;
        return RestackAbove(hwnd, sibling, h => GetWindow(h, 3 /* GW_HWNDPREV */),
            h => (GetWindowLong(h, GwlExStyle) & WsExTopmost) != 0,
            (h, after, flags) => SetWindowPos(h, after, 0, 0, 0, 0, flags));
    }

    // WPF AvatarTubeWindow.Windowing.cs:283-284 keeps the attached tube directly above main;
    // Win32 inserts BELOW hWndInsertAfter, so inserting after sibling would reverse RestackAbove.
    // Use the sibling's predecessor, without moving the sibling or its owner. Keep the sibling's topmost band at its boundary.
    internal static bool RestackAbove(IntPtr hwnd, IntPtr sibling, Func<IntPtr, IntPtr> previous,
        Func<IntPtr, bool> topmost, Func<IntPtr, IntPtr, uint, bool> position)
    {
        if (hwnd == IntPtr.Zero || sibling == IntPtr.Zero || hwnd == sibling) return false;
        var above = previous(sibling);
        var band = topmost(sibling);
        if (above == hwnd && topmost(hwnd) == band) return true;
        // WPF attached tubes must also shed stray TOPMOST (AvatarTubeWindow.Windowing.cs:277-284).
        if (above == IntPtr.Zero || topmost(above) != band)
            // NOTOPMOST is a no-op for an already non-topmost window. TOP raises it in that band.
            above = new IntPtr(band ? -1 : topmost(hwnd) ? -2 : 0);
        return position(hwnd, above, SwpNoMove | SwpNoSize | SwpNoActivate | 0x200 /* SWP_NOOWNERZORDER */);
    }

    // Per window: whether it should currently be click-through, read by the style callback.
    private static readonly ConditionalWeakTable<TopLevel, StrongBox<bool>> Wanted = new();

    // Passive toasts (achievement / item unlock): WS_EX_NOACTIVATE even while they take clicks, as
    // WPF Helpers.PassiveToastWindow - click-to-dismiss must not hand them the foreground.
    private static readonly ConditionalWeakTable<TopLevel, object> Passive = new();
    // Subliminal opt-in: keep TRANSPARENT but clear NOACTIVATE, including on Avalonia rebuilds.
    private static readonly ConditionalWeakTable<TopLevel, object> FocusAllowed = new();
    private static bool AllowsFocus(TopLevel window) => FocusAllowed.TryGetValue(window, out _);
    private static bool IsPassive(TopLevel window) => Passive.TryGetValue(window, out _);

    internal static uint Style(uint exStyle, bool clickThrough, bool passive = false, bool allowFocus = false)
        => (exStyle | OverlayBits) & ~ClickThroughBits
            | (clickThrough ? WsExTransparent : 0)
            | ((clickThrough && !allowFocus || passive) ? WsExNoActivate : 0);

    internal static bool SetClickThrough(TopLevel window, IntPtr hwnd, bool clickThrough)
    {
        if (!OperatingSystem.IsWindows()) return false;
        Track(window).Value = clickThrough;
        return Apply(hwnd, clickThrough, IsPassive(window), AllowsFocus(window));
    }

    /// <summary>Tool/no-activate/layered + HWND_TOPMOST: the Windows form of an override-redirect overlay.</summary>
    internal static bool SetOverrideRedirect(TopLevel window, IntPtr hwnd, bool passive = false)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (passive) Passive.AddOrUpdate(window, Passive);
        return Apply(hwnd, Track(window).Value, IsPassive(window), AllowsFocus(window))
            && SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>WPF ApplyWindowStyles(stealsFocus): activation and mouse pass-through are independent.</summary>
    internal static bool AllowFocus(TopLevel window, IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || IsPassive(window)) return false;
        FocusAllowed.AddOrUpdate(window, FocusAllowed);
        return Apply(hwnd, Track(window).Value, false, true);
    }

    internal static bool SetOpacity(TopLevel window, IntPtr hwnd, double alpha)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return Apply(hwnd, Track(window).Value, IsPassive(window), AllowsFocus(window))
            && SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), LwaAlpha);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointI p);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectI r);

    /// <summary>Is the window-relative px point inside any of the first <paramref name="count"/> rects?</summary>
    internal static bool Hits(global::Avalonia.PixelRect[] rects, int count, int x, int y)
    {
        for (var i = 0; i < count; i++)
        {
            var r = rects[i];   // half-open, like a Win32 RECT (PixelRect.Contains includes the far edge)
            if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height) return true;
        }
        return false;
    }

    /// <summary>The Windows form of an X11 input region. DirectComposition windows have no per-pixel
    /// hit-test and HTTRANSPARENT only passes clicks within the same thread, so a caller that re-sends
    /// its rects every step gets WS_EX_TRANSPARENT toggled by the cursor: input only while the pointer
    /// is over a rect, click-through everywhere else - the same answer WPF's per-pixel layered bubble
    /// windows gave. Only a changed state touches the window style.</summary>
    internal static bool SetInputRects(TopLevel window, IntPtr hwnd, global::Avalonia.PixelRect[] rects, int count)
    {
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var p) || !GetWindowRect(hwnd, out var r)) return false;
        var through = !Hits(rects, count, p.X - r.Left, p.Y - r.Top);
        var box = Track(window);
        if (box.Value == through && (GetWindowLong(hwnd, GwlExStyle) & WsExTransparent) != 0 == through) return true;
        return SetClickThrough(window, hwnd, through);
    }

    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    internal static bool SetWindowInputRegion(TopLevel window, IntPtr hwnd, global::Avalonia.PixelRect[]? rects)
    {
        if (!OperatingSystem.IsWindows() || !SetClickThrough(window, hwnd, false)) return false;
        if (rects is null) return SetWindowRgn(hwnd, IntPtr.Zero, true) != 0;
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero) return false;
        try
        {
            foreach (var r in rects)
            {
                var part = CreateRectRgn(r.X, r.Y, r.Right, r.Bottom);
                if (part == IntPtr.Zero) return false;
                try { if (CombineRgn(region, region, part, 2 /* RGN_OR */) == 0) return false; }
                finally { DeleteObject(part); }
            }
            if (SetWindowRgn(hwnd, region, true) == 0) return false;
            region = IntPtr.Zero; // user32 owns the region after success.
            return true;
        }
        finally { if (region != IntPtr.Zero) DeleteObject(region); }
    }

    private static StrongBox<bool> Track(TopLevel window) => Wanted.GetValue(window, w =>
    {
        var box = new StrongBox<bool>();
        Win32Properties.AddWindowStylesCallback(w, (style, ex) => (style, Style(ex, box.Value, IsPassive(w), AllowsFocus(w))));
        return box;
    });

    private static bool Apply(IntPtr hwnd, bool clickThrough, bool passive, bool allowFocus)
    {
        SetWindowLong(hwnd, GwlExStyle, Style(GetWindowLong(hwnd, GwlExStyle), clickThrough, passive, allowFocus));
        // Attributes never set = invisible layered window. Checked by attributes, not by the old
        // style bit, because the style callback can make a window layered behind our back.
        return GetLayeredWindowAttributes(hwnd, out _, out _, out _) || SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
    }
}
