using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private DispatcherTimer? _topmostWatch;
    internal bool TopmostWatchRunning => _topmostWatch?.IsEnabled == true;

    internal void StartTopmostWatch(bool windows)
    {
        if (!windows || _topmostWatch != null) return;
        _topmostWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(EmiTopmostRule.PollMs) };
        _topmostWatch.Tick += (_, _) =>
        {
            try
            {
                var handle = TryGetPlatformHandle();
                if (handle?.HandleDescriptor != "HWND") return;
                TopmostWatchTick(OperatingSystem.IsWindows(), handle.Handle, IsVisible, Topmost,
                    h => Win32Overlay.GetWindowLong(h, -20),
                    h => Win32Overlay.PositionWindow(h, new IntPtr(-1), 0, 0, 0, 0, 0x213));
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] topmost watch tick failed"); }
        };
        Closed += (_, _) => { _topmostWatch?.Stop(); _topmostWatch = null; };
        _topmostWatch.Start();
    }

    internal static void TopmostWatchTick(bool windows, IntPtr hwnd, bool visible, bool topmost,
        Func<IntPtr, uint> read, Func<IntPtr, bool> repair)
    {
        if (!windows || hwnd == IntPtr.Zero) return;
        if (EmiTopmostRule.NeedsRepair(visible, topmost, unchecked((int)read(hwnd)))) repair(hwnd);
    }
}
