using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF VideoService.cs:1392-1545: leased playback has its own ladder and cached handles.
internal sealed class BubbleCountWedgeWatchdog
{
    internal static readonly BubbleCountWedgeWatchdog Game = new();
    private readonly object _gate = new();
    private readonly HashSet<IntPtr> _windows = new();
    private readonly Func<long> _clock;
    private readonly Action<IntPtr[]> _release;
    private readonly Action _stop;
    private Timer? _timer;
    private DispatcherTimer? _heartbeat;
    private bool _armed;
    private long _lastBeat;
    private int _rung;

    internal BubbleCountWedgeWatchdog(Func<long>? clock = null, Action<IntPtr[]>? release = null, Action? stop = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
        _release = release ?? Release;
        _stop = stop ?? BubbleCountWindow.StopForWedge;
    }

    internal void Arm(bool startTimers = true)
    {
        Disarm();
        lock (_gate) { _armed = true; _lastBeat = _clock(); _rung = 0; }
        if (!startTimers) return;
        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeat.Tick += (_, _) => Beat();
        _heartbeat.Start();
        _timer = new Timer(_ => Tick(), null, 3000, 3000);
    }

    internal void Beat() { lock (_gate) { if (_armed) _lastBeat = _clock(); } }
    internal void Register(IntPtr id) { lock (_gate) { if (_armed && id != IntPtr.Zero) _windows.Add(id); } }
    internal void Unregister(IntPtr id) { lock (_gate) _windows.Remove(id); }
    internal bool IsArmed { get { lock (_gate) return _armed; } }
    internal void Tick()
    {
        IntPtr[]? ids = null;
        lock (_gate)
        {
            if (!_armed) return;
            var step = VideoWedgeLadder.NextRung(_clock() - _lastBeat, _rung, true, false);
            if (step == VideoWedgeLadder.WedgeAction.StopPlayers)
            {
                _rung = 1;
                try { _stop(); } catch (Exception ex) { Serilog.Log.Debug(ex, "Bubble count wedge stop failed"); }
            }
            else if (step == VideoWedgeLadder.WedgeAction.EscapeHatch)
            {
                _rung = 3;
                ids = new IntPtr[_windows.Count];
                _windows.CopyTo(ids);
            }
        }
        if (ids != null) _release(ids);
    }
    internal void Disarm()
    {
        lock (_gate) { _armed = false; _windows.Clear(); }
        _timer?.Dispose(); _timer = null;
        _heartbeat?.Stop(); _heartbeat = null;
    }

    internal static bool SupportsHandle(IPlatformHandle? handle) =>
        X11Overlay.BackendOf(handle) is OverlayBackend.X11 or OverlayBackend.Win32;

    internal bool RegisterHandle(IPlatformHandle? handle)
    {
        if (!SupportsHandle(handle)) return false;
        // Install the existing process-wide X error handler before stale XIDs can be encountered.
        if (X11Overlay.BackendOf(handle) == OverlayBackend.X11) _ = X11Overlay.IsAvailable;
        Register(handle!.Handle);
        return true;
    }

    internal void Register(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (!RegisterHandle(handle)) return;
        var id = handle!.Handle;
        window.Closed += (_, _) => Unregister(id);
    }

    private static void Release(IntPtr[] ids) =>
        Release(ids, OperatingSystem.IsWindows(),
            windows => ReleaseEach(windows, id => Win32Overlay.ReleaseWedgedWindow(id)),
            xids => ReleaseEach(xids, X11Overlay.ReleaseWedgedWindow));

    internal static void Release(IntPtr[] ids, bool windows, Action<IntPtr[]> releaseWindows, Action<IntPtr[]> releaseX11)
    {
        if (windows) releaseWindows(ids); else releaseX11(ids);
    }

    // The shared release helpers VideoWedgeWatchdog uses. A blocked window cannot hold up
    // the timer or another window's release.
    internal static void ReleaseEach(IntPtr[] ids, Action<IntPtr> release, Func<Action, Task>? run = null)
    {
        run ??= Task.Run;
        foreach (var id in ids)
            _ = run(() =>
            {
                try { release(id); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Bubble count window release failed"); }
            });
    }
}
