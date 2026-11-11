using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Windows twin of <see cref="X11Windows"/> for do-not-disturb, ported from WPF
/// Services/UI/DoNotDisturbGuard.cs (user32 foreground window, processes with a main window) by way of
/// parity/trunk 119ec005f / 2dbac1078. Compiled on every OS, only called on Windows; Core DndGuard
/// caches the foreground read for a second.
/// </summary>
internal static class Win32Windows
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    // WPF Windowing.cs:367: only another app's captionless, topmost popup covering its monitor.
    internal static bool OtherAppExclusiveFullscreen()
    {
        if (!OperatingSystem.IsWindows()) return X11Windows.OtherAppExclusiveFullscreen();
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return false;
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            var cls = name.ToString();
            string[] safe = { "Chrome_WidgetWin", "MozillaWindowClass", "ApplicationFrameWindow", "Windows.UI.Core", "CabinetWClass", "Shell_TrayWnd", "Progman", "WorkerW", "XLMAIN", "OpusApp", "PPTFrameClass", "VLC", "mpv", "MediaPlayerClassicW" };
            foreach (var prefix in safe) if (cls.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            int style = GetWindowLong(hwnd, -16), exStyle = GetWindowLong(hwnd, -20);
            if ((style & 0x00C00000) == 0x00C00000 || (style & unchecked((int)0x80000000)) == 0 || (exStyle & 8) == 0) return false;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetWindowRect(hwnd, out var rect) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return false;
            return rect.Left <= info.Monitor.Left + 5 && rect.Top <= info.Monitor.Top + 5 &&
                   rect.Right >= info.Monitor.Right - 5 && rect.Bottom >= info.Monitor.Bottom - 5;
        }
        catch { return false; }
    }

    /// <summary>The do-not-disturb sources for this OS: user32 on Windows, X11 elsewhere.</summary>
    internal static Func<string> ForegroundSource => OperatingSystem.IsWindows() ? ForegroundProcess : X11Windows.ForegroundProcess;
    internal static Func<List<string>> RunningAppsSource => OperatingSystem.IsWindows() ? RunningWindowedProcesses : X11Windows.RunningWindowedProcesses;

    /// <summary>Normalised process name owning the foreground window, or "" when unknown.</summary>
    internal static string ForegroundProcess()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById((int)pid);
            return DndProcessList.Normalize(p.ProcessName);
        }
        catch { return ""; }   // an unknown foreground reads as "not privileged"
    }

    /// <summary>WPF RunningWindowedProcesses: processes with a main window (roughly the taskbar), minus CCP.</summary>
    internal static List<string> RunningWindowedProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string self;
        try { self = DndProcessList.Normalize(Process.GetCurrentProcess().ProcessName); } catch { self = ""; }
        Process[] processes;
        try { processes = Process.GetProcesses(); } catch { return new List<string>(); }
        foreach (var p in processes)
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                var name = DndProcessList.Normalize(p.ProcessName);
                if (name.Length > 0 && name != self) names.Add(name);
            }
            catch { /* protected or exited */ }
            finally { p.Dispose(); }
        }
        return new List<string>(names);
    }
}
