using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>The Windows preference read by WPF SystemParameters.ClientAreaAnimation.</summary>
internal static class SystemMotion
{
    // Read on demand, as WPF does: changing the OS setting must affect the next state post.
    internal static Func<bool> AnimationsEnabled = ReadAnimationsEnabled;

    private static bool ReadAnimationsEnabled()
    {
        // ponytail: the reference has only a Windows OS cap; other platforms retain the app setting.
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            return !SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0,
                out var enabled, 0) || enabled;
        }
        catch { return true; } // WPF defaults to enabled when the preference cannot be read.
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
