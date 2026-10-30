using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static class ScreenMirrorHead
{
    private static DisplayMirror? _mirror;
    internal static DisplayMirror? Override; // Headless topology seam.
    internal static DisplayMirror? For(Visual host)
    {
        if (Override != null) return Override;
        if (!OperatingSystem.IsWindows()) return null;
        return _mirror ??= new DisplayMirror(new Win32DisplayTopology(() => ScreenList.Enumerate(host).Count));
    }
    internal static void Shutdown()
    {
        _mirror?.Dispose();
        _mirror = null;
    }
    [SupportedOSPlatform("windows")]
    private sealed class Win32DisplayTopology(Func<int> screenCount) : IDisplayTopology
    {
        private const uint SDC_TOPOLOGY_CLONE = 0x00000002;
        private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        private const uint SDC_APPLY = 0x00000080;
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray,
            uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);
        public int ActiveScreenCount => screenCount();
        public int Apply(bool clone) =>
            SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, (clone ? SDC_TOPOLOGY_CLONE : SDC_TOPOLOGY_EXTEND) | SDC_APPLY);
    }
}
