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
