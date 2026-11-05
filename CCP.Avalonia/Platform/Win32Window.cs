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
/// <item><see cref="DenyMouseActivate"/> - WPF Services/Video/VideoService.cs:8709 / NativeLayeredWindow.cs:131: answer
///   WM_MOUSEACTIVATE with MA_NOACTIVATE so a click does not activate (and raise) the window.</item>
/// <item><see cref="ApplyDarkTitleBar"/> - WPF Services/UI/WindowChromeHelper.cs: immersive dark mode and
///   the active mod's accent as caption/border colour, auto-contrast caption text; registered for every
///   window as WPF App.xaml.cs:2623 does, re-tinted on a mod switch (:2632).</item>
/// </list>
/// </summary>
internal static class Win32Window
{
    internal const uint WmMouseActivate = 0x21;
    internal const int MaNoActivate = 3;
    private const int DwmwaUseImmersiveDarkMode = 20, DwmwaBorderColor = 34, DwmwaCaptionColor = 35, DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static IntPtr Hwnd(TopLevel window)
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var h = window.TryGetPlatformHandle();
        return h?.HandleDescriptor == "HWND" ? h.Handle : IntPtr.Zero;
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
            PaintTitleBar(accent, (attribute, value) =>
                DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)));
        }
        catch { } // older Windows ignores unknown attributes; anything else is cosmetic
        return true;
    }

    internal static void PaintTitleBar((byte R, byte G, byte B) accent, Action<int, int> set)
    {
        var (caption, text) = TitleBarColors(accent);
        set(DwmwaUseImmersiveDarkMode, 1);
        set(DwmwaCaptionColor, caption);
        set(DwmwaBorderColor, caption);
        set(DwmwaTextColor, text);
    }

    private static bool _darkTitleBarsRegistered;

    /// <summary>WPF App.xaml.cs:2623-2640: every window gets the tinted title bar when it opens, and every
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
