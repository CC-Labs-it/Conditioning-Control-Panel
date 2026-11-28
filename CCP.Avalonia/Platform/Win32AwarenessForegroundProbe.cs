using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF AwarenessProbes.cs:22. Raw titles stay in the transient sample.
internal sealed class Win32AwarenessForegroundProbe : IForegroundProbe
{
    private uint _cachedPid;
    private string _cachedName = "";

    public ForegroundSample? Read()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            var title = new StringBuilder(512);
            var cls = new StringBuilder(128);
            GetWindowText(hwnd, title, title.Capacity);
            GetClassName(hwnd, cls, cls.Capacity);
            return new ForegroundSample(hwnd, title.ToString(), ProcessNameOf(hwnd),
                !IsShellClass(cls.ToString()) && CoversMonitor(hwnd));
        }
        catch { return null; }
    }

    internal static string NormalizeProcessName(string name)
    {
        name = (name ?? "").ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name.Substring(0, name.Length - 4) : name;
    }

    internal static bool IsShellClass(string name) =>
        string.Equals(name, "Progman", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase);

    internal static bool Covers(Rect window, Rect screen) =>
        window.Left <= screen.Left + 2 && window.Top <= screen.Top + 2 &&
        window.Right >= screen.Right - 2 && window.Bottom >= screen.Bottom - 2;

    private string ProcessNameOf(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            if (pid == _cachedPid && _cachedName.Length > 0) return _cachedName;
            using var process = Process.GetProcessById((int)pid);
            _cachedName = NormalizeProcessName(process.ProcessName);
            _cachedPid = pid;
            return _cachedName;
        }
        catch { return ""; }
    }

    private static bool CoversMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var window)) return false;
        var monitor = MonitorFromWindow(hwnd, 2);
        if (monitor == IntPtr.Zero) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfoW(monitor, ref info) && Covers(window, info.Monitor);
    }

    // WPF AvatarTubeWindow.Windowing.cs:367-462: external exclusive-style fullscreen only.
    private static readonly string[] TubeSafeClasses = { "Chrome_WidgetWin", "MozillaWindowClass", "ApplicationFrameWindow",
        "Windows.UI.Core", "CabinetWClass", "Shell_TrayWnd", "Progman", "WorkerW", "XLMAIN", "OpusApp",
        "PPTFrameClass", "VLC", "mpv", "MediaPlayerClassicW" };

    internal static bool TubeFullscreen(uint pid, uint ownPid, string cls, int style, int exStyle, Rect window, Rect screen)
    {
        if (pid == 0 || pid == ownPid) return false;
        foreach (var safe in TubeSafeClasses)
            if (cls.StartsWith(safe, StringComparison.OrdinalIgnoreCase)) return false;
        const int caption = 0x00c00000, popup = unchecked((int)0x80000000), topmost = 8;
        return (style & caption) != caption && (style & popup) == popup && (exStyle & topmost) == topmost &&
            window.Left <= screen.Left + 5 && window.Top <= screen.Top + 5 &&
            window.Right >= screen.Right - 5 && window.Bottom >= screen.Bottom - 5;
    }

    internal static bool ReadTubeFullscreen()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            var monitor = MonitorFromWindow(hwnd, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            return monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info) && TubeFullscreen(pid,
                (uint)Environment.ProcessId, cls.ToString(), GetWindowLong(hwnd, -16), GetWindowLong(hwnd, -20), rect, info.Monitor);
        }
        catch { return false; }
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
