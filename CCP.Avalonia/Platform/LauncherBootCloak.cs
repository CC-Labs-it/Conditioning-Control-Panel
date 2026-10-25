using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class LauncherBootCloak
{
    // WPF MainWindow.Launcher.cs: DWMWA_CLOAK, applied before Show.
    internal static void Set(Window window, bool cloaked)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = window.TryGetPlatformHandle();
        if (handle is not { Handle: var hwnd } || hwnd == IntPtr.Zero) return;
        try
        {
            int value = cloaked ? 1 : 0;
            int result = DwmSetWindowAttribute(hwnd, 13, ref value, sizeof(int));
            if (result != 0) Log.Debug("[Launcher] boot cloak returned {Result}", result);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] boot cloak failed"); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
