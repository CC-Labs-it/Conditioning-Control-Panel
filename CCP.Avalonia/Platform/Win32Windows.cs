using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
