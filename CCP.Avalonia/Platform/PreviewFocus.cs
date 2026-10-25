using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class PreviewFocus
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    internal static bool MayReactivate(uint? foregroundPid, uint ownPid)
        => foregroundPid == null || foregroundPid == ownPid;

    internal static void Reactivate(Window owner)
    {
        if (SandboxNet.Active) return;
        if (OperatingSystem.IsWindows())
        {
            var window = GetForegroundWindow();
            uint? pid = null;
            if (window != IntPtr.Zero) { GetWindowThreadProcessId(window, out var value); pid = value; }
            if (MayReactivate(pid, (uint)Environment.ProcessId)) owner.Activate();
        }
        // ponytail: without a portable foreground-process API, only activate an already active owner.
        else if (owner.IsActive) owner.Activate();
    }
}
