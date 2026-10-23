// PORTED from ConditioningControlPanel/Services/Video/ScreenMirrorService.cs (the SetDisplayConfig half; the state rules
// are Core DisplayMirror) and App.xaml.cs:3142/6305 (one instance, extend restored on exit).
// Only Windows has a display clone/extend switch an app may flip; on X11/Wayland/macOS there is no mirror, so
// fullscreen video plays on the shell's screen only, which is what WPF does when SetDisplayConfig refuses.
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

    /// <summary>Test seam: a mirror over a fake topology, so a headless test proves the fullscreen lifecycle.</summary>
    internal static DisplayMirror? Override;

    /// <summary>The app's mirror, or null where the OS has no clone mode. <paramref name="host"/> answers the
    /// active screen count (WPF App.GetAllScreensCached).</summary>
    internal static DisplayMirror? For(Visual host)
    {
        if (Override != null) return Override;
        if (!OperatingSystem.IsWindows()) return null;
        return _mirror ??= new DisplayMirror(new Win32DisplayTopology(() => ScreenList.Enumerate(host).Count));
    }

    /// <summary>WPF App.OnExit:6305 ScreenMirror?.Dispose(): a mirror left on would outlive the app.</summary>
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
