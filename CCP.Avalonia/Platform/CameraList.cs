using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF WebcamDeviceEnumerator.cs:51, MainWindow.LabTab.cs:1372.
// Both the picker and capture use this list, preserving DirectShow and V4L2 indices.
internal static class CameraList
{
    internal static Func<IReadOnlyList<(int Index, string Name)>> Provider = () =>
        EnumeratePlatform();
    internal static IReadOnlyList<(int Index, string Name)> Enumerate() => Provider();
    internal static IReadOnlyList<(int Index, string Name)> EnumeratePlatform() => OperatingSystem.IsWindows()
        ? EnumerateWindows()
        : V4l2Cameras.Enumerate();
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IReadOnlyList<(int Index, string Name)> EnumerateWindows() =>
        WebcamDeviceEnumerator.Enumerate().Select(d => (d.Index, d.Name)).ToArray();
}
