using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF SystemParameters.ClientAreaAnimation. Kept behind a seam; never call user32 off Windows.
internal static class MotionPreference
{
    internal static Func<bool> AnimationsEnabled = Read;
    private static bool Read()
    {
        // ponytail: the web-view package exposes no cross-platform animation preference;
        // non-Windows OS caps remain pending, matching the WPF default on query failure.
        if (!OperatingSystem.IsWindows()) return true;
        try { return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled; }
        catch { return true; }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
