using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Per-window Win32 treatments the WPF head applies, each a no-op unless the window's platform handle
/// is an HWND (so Linux and headless never change):
/// <list type="bullet">
/// <item><see cref="ApplyPassiveToast"/> - WPF Helpers/PassiveToastWindow.cs: WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW
///   and HWND_TOPMOST without activating, so a toast never steals focus or mouse capture (ccp-bugs #1000).</item>
/// <item><see cref="DenyMouseActivate"/> - WPF VideoService.cs:8971 / NativeLayeredWindow.cs:131: answer
///   WM_MOUSEACTIVATE with MA_NOACTIVATE so a click does not activate (and raise) the window.</item>
/// <item><see cref="ApplyDarkTitleBar"/> - WPF Services/UI/WindowChromeHelper.cs: immersive dark mode and
///   the active mod's accent as caption/border colour, auto-contrast caption text; registered for every
///   window as WPF App.xaml.cs:2559 does, re-tinted on a mod switch (:2567).</item>
/// </list>
/// </summary>
internal static class Win32Window
{
    private const int GwlExStyle = -20;
    internal const uint WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    internal const uint WmMouseActivate = 0x21;
    internal const int MaNoActivate = 3;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;
    private const int DwmwaUseImmersiveDarkMode = 20, DwmwaBorderColor = 34, DwmwaCaptionColor = 35, DwmwaTextColor = 36;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern uint SetWindowLong(IntPtr hwnd, int index, uint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static IntPtr Hwnd(TopLevel window)
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var h = window.TryGetPlatformHandle();
        return h?.HandleDescriptor == "HWND" ? h.Handle : IntPtr.Zero;
    }

    /// <summary>The PassiveToastWindow extended style: what WPF OR'd into GWL_EXSTYLE.</summary>
    internal static uint PassiveStyle(uint exStyle) => exStyle | WsExNoActivate | WsExToolWindow;

    /// <summary>WPF PassiveToastWindow.Apply. Call from the constructor; false when there is no HWND.</summary>
    internal static bool ApplyPassiveToast(Window window)
    {
        var hwnd = Hwnd(window);
        if (hwnd == IntPtr.Zero) return false;
        // Avalonia rebuilds GWL_EXSTYLE on ShowInTaskbar/state changes; the callback keeps the bits (see Win32Overlay).
        Win32Properties.AddWindowStylesCallback(window, (style, ex) => (style, PassiveStyle(ex)));
        SetWindowLong(hwnd, GwlExStyle, PassiveStyle(GetWindowLong(hwnd, GwlExStyle)));
        window.Opened += (_, _) => SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        return true;
    }

    /// <summary>The WM_MOUSEACTIVATE answer: MA_NOACTIVATE for that message, otherwise not handled.</summary>
    internal static IntPtr MouseActivate(uint msg, ref bool handled)
    {
        if (msg != WmMouseActivate) return IntPtr.Zero;
        handled = true;
        return new IntPtr(MaNoActivate);
    }

    /// <summary>WPF's HwndSource hook returning MA_NOACTIVATE; false when there is no HWND.</summary>
    internal static bool DenyMouseActivate(TopLevel window)
    {
        if (Hwnd(window) == IntPtr.Zero) return false;
        Win32Properties.AddWndProcHookCallback(window, (IntPtr _, uint msg, IntPtr _, IntPtr _, ref bool handled) => MouseActivate(msg, ref handled));
        return true;
    }

    /// <summary>WindowChromeHelper's colours as COLORREF (0x00BBGGRR): caption/border = accent, text dark
    /// ink on a bright accent and white on a dark one.</summary>
    internal static (int Caption, int Text) TitleBarColors((byte R, byte G, byte B) accent)
    {
        static int Colorref(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
        var (r, g, b) = accent;
        var luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        return (Colorref(r, g, b), luminance > 150 ? Colorref(0x16, 0x08, 0x26) : Colorref(0xFF, 0xFF, 0xFF));
    }

    /// <summary>WindowChromeHelper.ApplyDarkTitleBar; false when there is no HWND.</summary>
    internal static bool ApplyDarkTitleBar(Window window)
    {
        var hwnd = Hwnd(window);
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            // WPF fell back to #FF69B4 with no mod service; TryParseHexColor's default is the same.
            CoreMods.TryParseHexColor(CoreMods.AccentColorHex, out var accent);
            var (caption, text) = TitleBarColors(accent);
            int dark = 1, border = caption;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        }
        catch { } // older Windows ignores unknown attributes; anything else is cosmetic
        return true;
    }

    private static bool _darkTitleBarsRegistered;

    /// <summary>WPF App.xaml.cs:2559-2578: every window gets the tinted title bar when it opens, and every
    /// open window is re-tinted on a mod switch. Windows only.</summary>
    internal static void RegisterDarkTitleBars()
    {
        if (!OperatingSystem.IsWindows() || _darkTitleBarsRegistered) return;
        _darkTitleBarsRegistered = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => ApplyDarkTitleBar(w));
        CoreMods.ModChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var w in (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.ToArray() ?? [])
                ApplyDarkTitleBar(w);
        });
    }
}
