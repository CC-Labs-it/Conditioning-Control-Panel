using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MotionFx's ClientAreaAnimation cap, without a WPF dependency.</summary>
internal static class UiMotionPreference
{
    internal static MotionLevel ResolveLevel(MotionLevel setting, bool osAnimationsEnabled, bool gentleVisuals)
        => setting == MotionLevel.Off ? MotionLevel.Off
            : !osAnimationsEnabled || gentleVisuals ? MotionLevel.Reduced : setting;

    // WPF caches SystemParameters until WM_SETTINGCHANGE; per-frame readers here re-probe at most once a second.
    internal static Func<bool> OsProbe = ProbeOs; // test seam
    internal static long CacheStamp = long.MinValue;
    private static bool _cached = true;

    internal static bool AnimationsEnabled
    {
        get
        {
            var now = Environment.TickCount64;
            if (CacheStamp == long.MinValue || now - CacheStamp >= 1000)
            {
                _cached = OsProbe();
                CacheStamp = now;
            }
            return _cached;
        }
    }

    private static bool ProbeOs()
    {
        // ponytail: retain the selected level on non-Windows; this port supplies WPF's Windows cap.
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            // SPI_GETCLIENTAREAANIMATION: the same flag as SystemParameters.ClientAreaAnimation.
            return !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled != 0;
        }
        catch { return true; } // WPF also keeps the user's setting when the OS probe fails.
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
}
