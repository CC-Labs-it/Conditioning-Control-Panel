using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/UI/WindowChromeHelper.cs: native chrome stays native on every OS.
internal static class WindowChrome
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static void ApplyDarkTitleBar(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
            try
            {
                var (r, g, b) = App.Mods?.GetAccentColorRgb() ?? ((byte)0xFF, (byte)0x69, (byte)0xB4);
                ApplyAttributes(r, g, b, (attribute, value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)));
            }
            catch { /* Older Windows may lack these cosmetic attributes. */ }
        };
    }

    internal static void ApplyAttributes(byte r, byte g, byte b, Action<int, int> set)
    {
        int caption = r | (g << 8) | (b << 16); // COLORREF = 0x00BBGGRR
        set(20, 1); // DWMWA_USE_IMMERSIVE_DARK_MODE
        set(35, caption);
        set(34, caption);
        set(36, 0.299 * r + 0.587 * g + 0.114 * b > 150 ? 0x260816 : 0xFFFFFF);
    }

    internal static void RestoreOwnerOnClose(Window window)
    {
        Window? owner = null;
        window.Closing += (_, _) => owner = window.Owner as Window;
        window.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (owner is { IsVisible: true } && owner.WindowState != WindowState.Minimized) owner.Activate();
            }
            catch { /* Native teardown can race owner activation, as in WPF. */ }
        }, DispatcherPriority.ApplicationIdle);
    }
}
