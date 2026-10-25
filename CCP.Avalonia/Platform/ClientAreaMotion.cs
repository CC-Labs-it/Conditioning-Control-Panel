using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF MotionFx.Level reads SystemParameters.ClientAreaAnimation. Keep user32 in the head.
internal static class ClientAreaMotion
{
    internal static Func<bool> AnimationsEnabled = ReadAnimationsEnabled;

    internal static MotionLevel Resolve(MotionLevel setting, bool gentleVisuals)
    {
        if (setting == MotionLevel.Off) return MotionLevel.Off;
        return !AnimationsEnabled() || gentleVisuals ? MotionLevel.Reduced : setting;
    }

    private static bool ReadAnimationsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try { return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled; }
        catch { return true; } // WPF also keeps the user's setting when the OS read fails.
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
