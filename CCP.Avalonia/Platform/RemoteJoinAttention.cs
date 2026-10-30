using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF FlashWindowHelper: joining never activates or restores the subject's window.</summary>
internal static class RemoteJoinAttention
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags, Count, Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public static void Flash(Window window)
    {
        // ponytail: Linux has no Win32 taskbar; its join cue remains the OS notification.
        if (!OperatingSystem.IsWindows()) return;
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle,
            Flags = 3 | 12, Count = 5 }; // FLASHW_ALL | FLASHW_TIMERNOFG, WPF defaults
        FlashWindowEx(ref info);
    }
}
