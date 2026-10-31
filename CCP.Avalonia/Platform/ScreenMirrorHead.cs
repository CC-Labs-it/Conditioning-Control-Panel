using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static class ScreenMirrorHead {
    private static DisplayMirror? _mirror;
    private static WeakReference<Visual>? _lastHost;
    internal static DisplayMirror? Override; // Headless topology seam.
    internal static DisplayMirror? For(Visual host) => Override ?? (OperatingSystem.IsWindows() ? CachedFor(host) : null);
    internal static DisplayMirror CachedFor(Visual host) { _lastHost = new(host); return _mirror ??= new DisplayMirror(new Win32DisplayTopology(ActiveScreenCount)); }
    internal static int ActiveScreenCount() => _lastHost?.TryGetTarget(out var host) == true ? ScreenList.Enumerate(host).Count : 0;
    internal static void Shutdown() { _mirror?.Dispose(); _mirror = null; _lastHost = null; }
    private sealed class Win32DisplayTopology(Func<int> screenCount) : IDisplayTopology {
        private const uint SDC_TOPOLOGY_CLONE = 0x00000002;
        private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        private const uint SDC_APPLY = 0x00000080;
        [DllImport("user32.dll"), SupportedOSPlatform("windows")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray,
            uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);
        public int ActiveScreenCount => screenCount();
        public int Apply(bool clone) =>
            OperatingSystem.IsWindows() ? SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, (clone ? SDC_TOPOLOGY_CLONE : SDC_TOPOLOGY_EXTEND) | SDC_APPLY) : 50;
    }
}
